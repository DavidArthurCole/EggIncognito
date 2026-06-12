namespace EggIncognito.Bot;

// Bot configuration, built from the Discord:* config keys. GuildId is optional, used for the
// shared-role self-assign. RegisterGuildCommands is dev only: mirrors the catalog to GuildId for
// instant testing; see EnsureGuildCommandsAsync for why it must stay off in prod.
// DeployAgentUrl/Secret enable /updateserver; either missing = "Deploy agent not configured."
public sealed record BotOptions(
    string Token, string ApplicationId, string? GuildId, string RepoUrl, string? SharedRoleId,
    string? DeployAgentUrl = null, string? DeployAgentSecret = null, bool RegisterGuildCommands = false);
