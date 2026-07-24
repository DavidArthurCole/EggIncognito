namespace EggIncognito.GameData;

public sealed record Effect {
    public Effect(
        string family,
        string id,
        EffectTarget target,
        CombineMode combineMode,
        double magnitude,
        int? maxLevel = null,
        EffectSchema? metaSchema = null,
        IReadOnlyDictionary<string, object>? meta = null) {
        Family = family;
        Id = id;
        Target = target;
        CombineMode = combineMode;
        Magnitude = magnitude;
        MaxLevel = maxLevel;
        MetaSchema = metaSchema;
        Meta = meta ?? EmptyMeta;
        if (metaSchema is not null) {
            _ = new EffectRow(metaSchema, id, Meta);
        }
    }

    private static readonly IReadOnlyDictionary<string, object> EmptyMeta =
        new Dictionary<string, object>(0);

    public string Family { get; }
    public string Id { get; }
    public EffectTarget Target { get; }
    public CombineMode CombineMode { get; }
    public double Magnitude { get; }
    public int? MaxLevel { get; }
    public EffectSchema? MetaSchema { get; }
    public IReadOnlyDictionary<string, object> Meta { get; }

    public double Contribution(int level) => Magnitude * level;

    public double MetaDouble(string field) => Convert.ToDouble(Meta[field]);
    public int MetaInt(string field) => Convert.ToInt32(Meta[field]);
    public string MetaString(string field) => (string)Meta[field];
    public bool MetaBool(string field) => (bool)Meta[field];
    public bool TryMeta(string field, out object value) => Meta.TryGetValue(field, out value!);
}
