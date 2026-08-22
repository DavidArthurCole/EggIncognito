namespace EggIncognito.Models.Registry;

public sealed record ReleaseEditRow(string Platform, string Build, string AppVersion, string? ClientVersion, string Source);
