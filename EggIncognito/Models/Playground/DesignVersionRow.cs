namespace EggIncognito.Models.Playground;

public record DesignVersionRow(int VersionNo, string? Note, DateTimeOffset CreatedAt, Guid? AuthorUserId, int? RolledBackFrom);
