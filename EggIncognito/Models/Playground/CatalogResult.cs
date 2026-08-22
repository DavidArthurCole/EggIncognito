namespace EggIncognito.Models.Playground;

public record CatalogResult(bool Ok, string? Platform, int AssetTypeCount, CatalogElement[]? Elements, string? Diagnostics);
