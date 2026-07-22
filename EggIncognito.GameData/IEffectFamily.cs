namespace EggIncognito.GameData;

public interface IEffectFamily {
    string Key { get; }
    EffectSchema? MetaSchema { get; }
    IReadOnlyList<Effect> Effects { get; }
    IReadOnlyDictionary<string, ProvenanceSource> Provenance { get; }
    Effect? Find(string id);
}
