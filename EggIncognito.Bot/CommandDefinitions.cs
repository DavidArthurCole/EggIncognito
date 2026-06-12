using Discord;

namespace EggIncognito.Bot;

// The slash-command catalog. Every command is global + user-installable + usable in guilds, bot DMs,
// and private channels, so it runs anywhere the bot or user has it installed.
public static class CommandDefinitions
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

    public static ApplicationCommandProperties[] BuildAll() => new[]
    {
        Base("health", "Liveness check - pong + uptime.").Build(),
        Base("status", "Show the running server's live status (mode, capture, DB, signing, uptime).").Build(),
        Base("verify", "Show the server's build identity (SHA, version, build date).").Build(),
        Base("endpoints", "Show endpoint coverage (ok / empty / missing).").Build(),
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
        // Deploy trigger. Deliberately NOT user-installable and guild-only: the gate is the guild
        // Administrator permission, which only exists in a guild context.
        new SlashCommandBuilder()
            .WithName("updateserver").WithDescription("Pull latest and redeploy (admin only).")
            .WithIntegrationTypes(ApplicationIntegrationType.GuildInstall)
            .WithContextTypes(InteractionContextType.Guild)
            .WithDefaultMemberPermissions(GuildPermission.Administrator)
            .Build(),
    };
}
