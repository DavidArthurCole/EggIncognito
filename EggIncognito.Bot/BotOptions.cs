namespace EggIncognito.Bot;

// Bot configuration, built from the Discord:* config keys. GuildId is optional - when set, commands
// are ALSO registered to that guild for instant testing (global registration can take ~1h to appear).
public sealed record BotOptions(string Token, string ApplicationId, string? GuildId, string RepoUrl, string? SharedRoleId);
