using Discord;
using Discord.WebSocket;
using EggIncognito.Services;
using Microsoft.Extensions.Logging;

namespace EggIncognito.Bot;

// Routes slash-command + autocomplete interactions to handlers. Every slash command is deferred
// up front (status.Build() + proto reflection can outrun Discord's 3s interaction window) and
// answered with ephemeral followups. Each command is wrapped in try/catch so one failing
// interaction never tears down the gateway connection - the user gets a fixed generic error embed
// and the exception details stay in the server log. Pure dispatch/parsing lives in CommandParsing.
public sealed class InteractionRouter(
    IStatusProvider status,
    IProtoReflection proto,
    DateTimeOffset startedAt,
    ILogger logger)
{
    private const string GenericError = "The command failed on the server. Details are in the server log.";

    public async Task HandleSlashAsync(SocketSlashCommand cmd)
    {
        try
        {
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
