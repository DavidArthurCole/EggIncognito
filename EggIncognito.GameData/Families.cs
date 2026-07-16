namespace EggIncognito.GameData;

public static class Families
{
    public const string Boost = "boost";
    public const string Research = "research";
    public const string Hab = "hab";
    public const string Artifact = "artifact";
}

public abstract class EmbeddedEffectFamily : IEffectFamily
{
    private readonly Dictionary<string, Effect> _byId;

    protected EmbeddedEffectFamily(string key, string resource, EffectSchema? metaSchema)
    {
        Key = key;
        MetaSchema = metaSchema;
        var file = EffectDataLoader.Read(resource);
        Effects = EffectDataLoader.ToEffects(key, file, metaSchema);
        BinaryVersion = file.BinaryVersion;
        Status = file.Status;
        _byId = Effects.ToDictionary(e => e.Id, StringComparer.Ordinal);
    }

    public string Key { get; }
    public EffectSchema? MetaSchema { get; }
    public IReadOnlyList<Effect> Effects { get; }
    public string BinaryVersion { get; }
    public string Status { get; }

    public Effect? Find(string id) => _byId.GetValueOrDefault(id);
}

public sealed class BoostFamily() : EmbeddedEffectFamily(Families.Boost, "boosts.json", MetaSchemaDef)
{
    private static readonly EffectSchema MetaSchemaDef = new([
        new EffectField("kind", EffectFieldType.String),
        new EffectField("durationSeconds", EffectFieldType.Int),
        new EffectField("multiplier", EffectFieldType.Double, Required: false),
        new EffectField("appliesTo", EffectFieldType.String, Required: false),
        new EffectField("slotCap", EffectFieldType.Int, Required: false)
    ]);

    public static BoostFamily Load() => new();
}

public sealed class ResearchFamily() : EmbeddedEffectFamily(Families.Research, "research.json", MetaSchemaDef)
{
    private static readonly EffectSchema MetaSchemaDef = new([
        new EffectField("name", EffectFieldType.String, Required: false),
        new EffectField("epic", EffectFieldType.Bool, Required: false)
    ]);

    public static ResearchFamily Load() => new();
}

public sealed class HabFamily() : EmbeddedEffectFamily(Families.Hab, "habs.json", MetaSchemaDef)
{
    private static readonly EffectSchema MetaSchemaDef = new([
        new EffectField("habId", EffectFieldType.Int),
        new EffectField("name", EffectFieldType.String, Required: false)
    ]);

    public static HabFamily Load() => new();
}

public sealed class ArtifactFamily() : EmbeddedEffectFamily(Families.Artifact, "artifacts.json", MetaSchemaDef)
{
    private static readonly EffectSchema MetaSchemaDef = new([
        new EffectField("boost", EffectFieldType.String),
        new EffectField("tier", EffectFieldType.Int, Required: false),
        new EffectField("rarity", EffectFieldType.Int, Required: false),
        new EffectField("stoneCap", EffectFieldType.Int, Required: false)
    ]);

    public static ArtifactFamily Load() => new();
}
