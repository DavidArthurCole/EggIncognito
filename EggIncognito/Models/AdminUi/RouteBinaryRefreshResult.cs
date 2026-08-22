namespace EggIncognito.Models.AdminUi;

public record RouteBinaryRefreshResult(int Discovered, int New, int DriftCount, string? BinaryVersion, string? Note);
