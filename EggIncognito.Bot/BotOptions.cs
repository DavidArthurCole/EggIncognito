namespace EggIncognito.Bot;

// Bot configuration, built from the Discord:* config keys.
public sealed record BotOptions(
    string Token, string ApplicationId, string? GuildId, string RepoUrl, string? SharedRoleId,
    string? DeployAgentUrl = null, string? DeployAgentSecret = null, bool RegisterGuildCommands = false);
