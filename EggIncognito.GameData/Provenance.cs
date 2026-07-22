namespace EggIncognito.GameData;

public sealed record ProvenanceSource(string Origin, string? Locator = null, string? Method = null);

public static class Provenance {
    public static readonly IReadOnlyDictionary<string, ProvenanceSource> Empty =
        new Dictionary<string, ProvenanceSource>(0, StringComparer.Ordinal);
}
