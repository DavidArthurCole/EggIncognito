namespace EggIncognito.Models.Notifications;

public sealed record DiscordEmbed(
    string? Title,
    string? Url,
    string? Description,
    string? Author,
    string? Footer,
    string? Stamp,
    int? Color,
    List<DiscordEmbedField> Fields);
