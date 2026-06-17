using Discord;
using Discord.WebSocket;
using EggIncognito.Services;
using Microsoft.Extensions.Logging;

namespace EggIncognito.Bot;

// Slash-command + autocomplete router. Defers all interactions up front (status.Build() can exceed Discord's 3s window).
// Per-command try/catch so one failure never tears down the gateway; failing commands get a generic error embed.
public sealed class InteractionRouter(
    IStatusProvider status,
    IProtoReflection proto,
    DateTimeOffset startedAt,
    ILogger logger,
    DeployAgentClient? deploy = null)
{
    private const string GenericError = "The command failed on the server. Details are in the server log.";

    public async Task HandleSlashAsync(SocketSlashCommand cmd)
    {
        try
        {
            // /updateserver owns its response lifecycle (ephemeral gate replies, public defer),
            // so it branches before the blanket ephemeral defer.
            if (CommandParsing.Resolve(cmd.CommandName) == BotCommand.UpdateServer)
            {
                await HandleUpdateServerAsync(cmd);
                return;
            }
            await cmd.DeferAsync(ephemeral: true);
            switch (CommandParsing.Resolve(cmd.CommandName))
            {
                case BotCommand.Health:
                    await cmd.FollowupAsync(embed: BotEmbeds.Health(DateTimeOffset.UtcNow - startedAt), ephemeral: true);
                    break;
                case BotCommand.Status:
                    await cmd.FollowupAsync(embed: BotEmbeds.Status(status.Build()), ephemeral: true);
                    break;
                case BotCommand.Verify:
                    await cmd.FollowupAsync(embed: BotEmbeds.Verify(status.Build()), ephemeral: true);
                    break;
                case BotCommand.Endpoints:
                    await cmd.FollowupAsync(embed: BotEmbeds.Endpoints(status.Build()), ephemeral: true);
                    break;
                case BotCommand.Proto:
                    await HandleProtoAsync(cmd);
                    break;
                default:
                    await cmd.FollowupAsync(embed: BotEmbeds.Error($"Unknown command /{cmd.CommandName}."), ephemeral: true);
                    break;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "bot: command /{Command} failed", cmd.CommandName);
            try
            {
                if (cmd.HasResponded)
                    await cmd.FollowupAsync(embed: BotEmbeds.Error(GenericError), ephemeral: true);
                else
                    await cmd.RespondAsync(embed: BotEmbeds.Error(GenericError), ephemeral: true);
            }
            catch { /* gateway race - nothing more to do */ }
        }
    }

    // Ephemeral refusal for non-admin/no-agent; public defer + success/failure embeds for a real run.
    // Runtime permission re-check is defense-in-depth against rebound permissions.
    private async Task HandleUpdateServerAsync(SocketSlashCommand cmd)
    {
        if (cmd.User is not SocketGuildUser invoker || !invoker.GuildPermissions.Administrator)
        {
            await cmd.RespondAsync("Not authorized.", ephemeral: true);
            return;
        }
        if (deploy is null)
        {
            await cmd.RespondAsync("Deploy agent not configured.", ephemeral: true);
            return;
        }
        await cmd.DeferAsync();
        var res = await deploy.DeployAsync();
        if (res.Ok && res.AlreadyUpToDate)
        {
            await cmd.FollowupAsync(embed: BotEmbeds.UpdateAlreadyCurrent(res.FromHash, res.FromUrl));
            return;
        }
        if (res.Ok)
        {
            await cmd.FollowupAsync(embed: BotEmbeds.UpdateSuccess(res.FromHash, res.ToHash, res.FromUrl, res.ToUrl));
            return;
        }
        await cmd.DeleteOriginalResponseAsync();
        await cmd.FollowupAsync(embed: BotEmbeds.UpdateFailure(res.Tail), ephemeral: true);
    }

    private async Task HandleProtoAsync(SocketSlashCommand cmd)
    {
        var sub = cmd.Data.Options.FirstOrDefault();
        List<(string Name, object? Value)> opts = sub?.Options?
            .Select(o => (o.Name, (object?)o.Value))
            .ToList() ?? new();
        var args = CommandParsing.ParseProto(sub?.Name, opts);
        if (args.Error is not null)
        {
            await cmd.FollowupAsync(embed: BotEmbeds.Error(args.Error), ephemeral: true);
            return;
        }
        if (args.IsList)
        {
            var (slice, p, pages) = ProtoQuery.Page(proto.AllMessageTypeNames(), args.Page);
            var embed = new EmbedBuilder()
                .WithTitle($"Proto message types (page {p}/{pages})")
                .WithColor(new Color(0xEF7559))
                .WithDescription(slice.Count == 0 ? "(none)" : ProtoQuery.Truncate(string.Join("\n", slice)))
                .Build();
            await cmd.FollowupAsync(embed: embed, ephemeral: true);
            return;
        }
        var schema = proto.Schema(args.TypeName!);
        if (schema is null)
        {
            await cmd.FollowupAsync(embed: BotEmbeds.Error($"Unknown proto type `{args.TypeName}`."), ephemeral: true);
            return;
        }
        var detail = new EmbedBuilder()
            .WithTitle($"Ei.{schema.Name}")
            .WithColor(new Color(0xEF7559))
            .WithDescription("```\n" + ProtoQuery.Truncate(ProtoQuery.TypeLines(schema)) + "\n```")
            .Build();
        await cmd.FollowupAsync(embed: detail, ephemeral: true);
    }

    public async Task HandleAutocompleteAsync(SocketAutocompleteInteraction ac)
    {
        try
        {
            if (ac.Data.CommandName != "proto") { await ac.RespondAsync(Array.Empty<AutocompleteResult>()); return; }
            var current = ac.Data.Current.Value?.ToString() ?? "";
            var hits = ProtoQuery.Autocomplete(proto.AllMessageTypeNames(), current)
                .Select(n => new AutocompleteResult(n, n));
            await ac.RespondAsync(hits);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "bot: autocomplete failed");
            try { await ac.RespondAsync(Array.Empty<AutocompleteResult>()); } catch { /* race */ }
        }
    }
}
