namespace EggIncognito.GameData;

public abstract class GameDataCatalog<TEntry, TKey> where TEntry : class where TKey : notnull {
    private readonly Dictionary<TKey, TEntry> _byKey;

    protected GameDataCatalog(IReadOnlyList<TEntry> entries, string version,
        IReadOnlyDictionary<string, ProvenanceSource> provenance, Func<TEntry, TKey> keyOf,
        IEqualityComparer<TKey>? comparer = null) {
        Entries = entries;
        Version = version;
        Provenance = provenance;
        _byKey = entries.ToDictionary(keyOf, comparer);
    }

    public IReadOnlyDictionary<string, ProvenanceSource> Provenance { get; }

    protected IReadOnlyList<TEntry> Entries { get; }

    protected string Version { get; }

    protected TEntry? FindByKey(TKey key) => _byKey.GetValueOrDefault(key);

    protected bool ContainsKey(TKey key) => _byKey.ContainsKey(key);
}
