namespace EggIncognito.Models.AdminUi;

public record UserRow(string DiscordId, string Username, string Role, List<string>? Providers, DateTimeOffset LastLoginAt);
