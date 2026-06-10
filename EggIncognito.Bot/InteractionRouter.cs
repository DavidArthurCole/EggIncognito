using Discord;
using Discord.WebSocket;
using EggIncognito.Services;
using Microsoft.Extensions.Logging;

namespace EggIncognito.Bot;

// Routes slash-command + autocomplete interactions to handlers. Each command is wrapped in try/catch
// so one failing interaction never tears down the gateway connection - it replies with an ephemeral
// error embed instead. Reads live data via the status provider + proto reflection.
public sealed class InteractionRouter(
    IStatusProvider status,
    IProtoReflection proto,
    DateTimeOffset startedAt,
    ILogger logger)
{
    public async Task HandleSlashAsync(SocketSlashCommand cmd)
    {
        try
        {
            switch (cmd.CommandName)
            {
                case "health":
                    await cmd.RespondAsync(embed: BotEmbeds.Health(DateTimeOffset.UtcNow - startedAt), ephemeral: true);
                    break;
                case "status":
                    await cmd.RespondAsync(embed: BotEmbeds.Status(status.Build()), ephemeral: true);
                    break;
                case "verify":
                    await cmd.RespondAsync(embed: BotEmbeds.Verify(status.Build()), ephemeral: true);
                    break;
                case "endpoints":
                    await cmd.RespondAsync(embed: BotEmbeds.Endpoints(status.Build()), ephemeral: true);
                    break;
                case "proto":
                    await HandleProtoAsync(cmd);
                    break;
                default:
                    await cmd.RespondAsync(embed: BotEmbeds.Error($"Unknown command /{cmd.CommandName}."), ephemeral: true);
                    break;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "bot: command /{Command} failed", cmd.CommandName);
            try
            {
                if (cmd.HasResponded)
                    await cmd.FollowupAsync(embed: BotEmbeds.Error(ex.Message), ephemeral: true);
                else
                    await cmd.RespondAsync(embed: BotEmbeds.Error(ex.Message), ephemeral: true);
            }
            catch { /* gateway race - nothing more to do */ }
        }
    }

    private async Task HandleProtoAsync(SocketSlashCommand cmd)
    {
        var sub = cmd.Data.Options.First();
        if (sub.Name == "list")
        {
            var page = (int)(long)(sub.Options.FirstOrDefault(o => o.Name == "page")?.Value ?? 1L);
            var (slice, p, pages) = ProtoQuery.Page(proto.AllMessageTypeNames(), page);
            var embed = new EmbedBuilder()
                .WithTitle($"Proto message types (page {p}/{pages})")
                .WithColor(new Color(0xEF7559))
                .WithDescription(slice.Count == 0 ? "(none)" : string.Join("\n", slice))
                .Build();
            await cmd.RespondAsync(embed: embed, ephemeral: true);
        }
        else
        {
            var name = (string)sub.Options.First(o => o.Name == "name").Value;
            var schema = proto.Schema(name);
            if (schema is null)
            {
                await cmd.RespondAsync(embed: BotEmbeds.Error($"Unknown proto type `{name}`."), ephemeral: true);
                return;
            }
            var embed = new EmbedBuilder()
                .WithTitle($"Ei.{schema.Name}")
                .WithColor(new Color(0xEF7559))
                .WithDescription("```\n" + ProtoQuery.TypeLines(schema) + "\n```")
                .Build();
            await cmd.RespondAsync(embed: embed, ephemeral: true);
        }
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
