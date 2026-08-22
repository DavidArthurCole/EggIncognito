namespace EggIncognito.Models.Periodicals;

public record FeedInfo(string Name, string Path, bool Present, long Bytes, DateTimeOffset? UpdatedAt);
