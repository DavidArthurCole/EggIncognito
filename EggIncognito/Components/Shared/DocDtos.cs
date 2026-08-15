namespace EggIncognito.Components.Shared;

public sealed record DocResult(string? BodyMd);

public sealed record TagRow(long Id, string Slug, string Label, string? Color);
