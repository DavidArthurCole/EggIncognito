namespace EggIncognito.GameData;

public sealed class GameDataProvider : IGameDataProvider
{
    private readonly Dictionary<string, IEffectFamily> _byKey;

    public GameDataProvider(IReadOnlyList<IEffectFamily> families, IColleggtibleCatalog colleggtibles)
    {
        Families = families;
        Colleggtibles = colleggtibles;
        _byKey = families.ToDictionary(f => f.Key, StringComparer.Ordinal);
    }

    public static GameDataProvider CreateDefault() =>
        new([
            BoostFamily.Load(),
            ResearchFamily.Load(),
            HabFamily.Load(),
            ArtifactFamily.Load()
        ], ColleggtibleCatalog.Load());

    public IReadOnlyList<IEffectFamily> Families { get; }
    public IColleggtibleCatalog Colleggtibles { get; }

    public IEffectFamily? Family(string key) => _byKey.GetValueOrDefault(key);

    public Effect? Resolve(string family, string id) => Family(family)?.Find(id);

    public bool TryResolve(string family, string id, out Effect effect)
    {
        var found = Resolve(family, id);
        effect = found!;
        return found is not null;
    }

    public IReadOnlyList<Effect> All(string family) =>
        Family(family)?.Effects ?? [];

    public IReadOnlyList<Effect> ByTarget(EffectTarget target) =>
        Families.SelectMany(f => f.Effects).Where(e => e.Target == target).ToArray();

    public double Effective(EffectTarget target, double seed, IReadOnlyDictionary<string, int> idLevels)
    {
        var active = new List<Effect>();
        foreach (var (id, _) in idLevels)
        {
            foreach (var family in Families)
            {
                var effect = family.Find(id);
                if (effect is not null && effect.Target == target)
                {
                    active.Add(effect);
                }
            }
        }

        var value = seed;
        foreach (var group in active.GroupBy(e => e.CombineMode))
        {
            var contributions = group.Select(e => e.Contribution(LevelOf(idLevels, e.Id)));
            value = Folding.Fold(group.Key, value, contributions);
        }
        return value;
    }

    private static int LevelOf(IReadOnlyDictionary<string, int> idLevels, string id) =>
        idLevels.TryGetValue(id, out var level) ? level : 1;
}
