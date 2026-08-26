namespace EggIncognito.Models.Admin;

public sealed record GameDataRebuildResponse(List<GameDataRebuildDocResult> Results, string? Binary, List<string> Missing);
