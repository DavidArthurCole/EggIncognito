using Discord;
using Discord.WebSocket;
using SyncKit.Bot;

namespace EggIncognito.Bot;

// Domain slash commands (health/status/endpoints/proto), wired as SyncKit.Bot.BotCommand entries
// via BotConfig.Extra. Definitions are global + user-installable + usable in DMs, matching the
// scope EggIncognito registered on its own gateway host before the SyncKit cutover.
public static class ExtraCommands
{
    private static readonly ApplicationIntegrationType[] Integrations =
        { ApplicationIntegrationType.GuildInstall, ApplicationIntegrationType.UserInstall };
    private static readonly InteractionContextType[] Contexts =
        { InteractionContextType.Guild, InteractionContextType.BotDm, InteractionContextType.PrivateChannel };

    private static SlashCommandBuilder Base(string name, string desc) =>
        new SlashCommandBuilder()
            .WithName(name).WithDescription(desc)
            .WithIntegrationTypes(Integrations)
            .WithContextTypes(Contexts);

    public static SyncKit.Bot.BotCommand HealthCommand(DateTimeOffset startedAt) =>
        new SyncKit.Bot.BotCommand(Base("health", "Liveness check - pong + uptime.").Build(), "health",
            async (SocketSlashCommandContext ctx) => await RunAsync(ctx, () => BotEmbeds.Health(DateTimeOffset.UtcNow - startedAt)));

    public static SyncKit.Bot.BotCommand StatusCommand(IStatusProvider status) =>
        new SyncKit.Bot.BotCommand(Base("status", "Show the running server's live status (mode, capture, DB, signing, uptime).").Build(), "status",
            async (SocketSlashCommandContext ctx) => await RunAsync(ctx, () => BotEmbeds.Status(status.Build())));

    public static SyncKit.Bot.BotCommand EndpointsCommand(IStatusProvider status) =>
        new SyncKit.Bot.BotCommand(Base("endpoints", "Show endpoint coverage (ok / empty / missing).").Build(), "endpoints",
            async (SocketSlashCommandContext ctx) => await RunAsync(ctx, () => BotEmbeds.Endpoints(status.Build())));

    // Deferring here (not left to SyncKitBot, which has no dispatch-level try/catch around Extra
    // handlers) matches every handler needing the same ephemeral-defer-then-followup shape.
    private static async Task RunAsync(SocketSlashCommandContext ctx, Func<Embed> build)
    {
        await ctx.Command.DeferAsync(ephemeral: true);
        try
        {
            await ctx.Command.FollowupAsync(embed: build(), ephemeral: true);
        }
        catch (Exception)
        {
            await ctx.Command.FollowupAsync(
                embed: BotEmbeds.Error("The command failed on the server. Details are in the server log."),
                ephemeral: true);
        }
    }

    public static SyncKit.Bot.BotCommand ProtoCommand(EggIncognito.Services.IProtoReflection proto) =>
        new(
            Base("proto", "Look up Egg, Inc. proto message types.")
                .AddOption(new SlashCommandOptionBuilder()
                    .WithName("list").WithDescription("List proto message types.")
                    .WithType(ApplicationCommandOptionType.SubCommand)
                    .AddOption("page", ApplicationCommandOptionType.Integer, "Page number.", isRequired: false))
                .AddOption(new SlashCommandOptionBuilder()
                    .WithName("type").WithDescription("Show one message type's fields.")
                    .WithType(ApplicationCommandOptionType.SubCommand)
                    .AddOption(new SlashCommandOptionBuilder()
                        .WithName("name").WithDescription("Message type name.")
                        .WithType(ApplicationCommandOptionType.String)
                        .WithRequired(true)
                        .WithAutocomplete(true)))
                .Build(),
            "proto",
            ctx => HandleProtoAsync(ctx, proto),
            ctx => HandleProtoAutocompleteAsync(ctx, proto));

    private static async Task HandleProtoAsync(SocketSlashCommandContext ctx, EggIncognito.Services.IProtoReflection proto)
    {
        var cmd = ctx.Command;
        await cmd.DeferAsync(ephemeral: true);
        try
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
        catch (Exception)
        {
            await cmd.FollowupAsync(
                embed: BotEmbeds.Error("The command failed on the server. Details are in the server log."),
                ephemeral: true);
        }
    }

    private static async Task HandleProtoAutocompleteAsync(SocketAutocompleteContext ctx, EggIncognito.Services.IProtoReflection proto)
    {
        var ac = ctx.Interaction;
        try
        {
            if (ac.Data.CommandName != "proto") { await ac.RespondAsync(Array.Empty<AutocompleteResult>()); return; }
            var current = ac.Data.Current.Value?.ToString() ?? "";
            var hits = ProtoQuery.Autocomplete(proto.AllMessageTypeNames(), current)
                .Select(n => new AutocompleteResult(n, n));
            await ac.RespondAsync(hits);
        }
        catch (Exception) { try { await ac.RespondAsync(Array.Empty<AutocompleteResult>()); } catch { } }
    }
}
