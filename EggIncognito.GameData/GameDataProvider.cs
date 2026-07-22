namespace EggIncognito.GameData;

public sealed class GameDataProvider(IReadOnlyList<IEffectFamily> families, IColleggtibleCatalog colleggtibles, IBoostCatalog boostCatalog, IEggCatalog eggCatalog, IDimensionCatalog dimensions, IMissionCatalog missions, IVehicleCatalog vehicles) : IGameDataProvider {
    private readonly Dictionary<string, IEffectFamily> _byKey = families.ToDictionary(f => f.Key, StringComparer.Ordinal);

    public static GameDataProvider CreateDefault() =>
        new([
            BoostFamily.Load(),
            ResearchFamily.Load(),
            HabFamily.Load(),
            ArtifactFamily.Load()
        ], ColleggtibleCatalog.Load(), global::EggIncognito.GameData.BoostCatalog.Load(), global::EggIncognito.GameData.EggCatalog.Load(), DimensionCatalog.Load(), MissionCatalog.Load(), VehicleCatalog.Load());

    public IReadOnlyList<IEffectFamily> Families { get; } = families;
    public IColleggtibleCatalog Colleggtibles { get; } = colleggtibles;
    public IBoostCatalog BoostCatalog { get; } = boostCatalog;
    public IEggCatalog EggCatalog { get; } = eggCatalog;
    public IDimensionCatalog Dimensions { get; } = dimensions;
    public IMissionCatalog Missions { get; } = missions;
    public IVehicleCatalog Vehicles { get; } = vehicles;

    public IEffectFamily? Family(string key) => _byKey.GetValueOrDefault(key);

    public Effect? Resolve(string family, string id) => Family(family)?.Find(id);

    public bool TryResolve(string family, string id, out Effect effect) {
        var found = Resolve(family, id);
        effect = found!;
        return found is not null;
    }

    public IReadOnlyList<Effect> All(string family) =>
        Family(family)?.Effects ?? [];

    public IReadOnlyList<Effect> ByTarget(EffectTarget target) =>
        Families.SelectMany(f => f.Effects).Where(e => e.Target == target).ToArray();

    public double Effective(EffectTarget target, double seed, IReadOnlyDictionary<string, int> idLevels) {
        var active = new List<Effect>();
        foreach (var (id, _) in idLevels) {
            foreach (var family in Families) {
                var effect = family.Find(id);
                if (effect is not null && effect.Target == target) {
                    active.Add(effect);
                }
            }
        }

        var value = seed;
        foreach (var group in active.GroupBy(e => e.CombineMode)) {
            var contributions = group.Select(e => e.Contribution(LevelOf(idLevels, e.Id)));
            value = Folding.Fold(group.Key, value, contributions);
        }
        return value;
    }

    private static int LevelOf(IReadOnlyDictionary<string, int> idLevels, string id) =>
        idLevels.TryGetValue(id, out var level) ? level : 1;
}
