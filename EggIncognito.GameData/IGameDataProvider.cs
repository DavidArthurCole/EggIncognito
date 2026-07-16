namespace EggIncognito.GameData;

public interface IGameDataProvider
{
    IReadOnlyList<IEffectFamily> Families { get; }
    IColleggtibleCatalog Colleggtibles { get; }
    IEffectFamily? Family(string key);
    Effect? Resolve(string family, string id);
    bool TryResolve(string family, string id, out Effect effect);
    IReadOnlyList<Effect> All(string family);
    IReadOnlyList<Effect> ByTarget(EffectTarget target);
    double Effective(EffectTarget target, double seed, IReadOnlyDictionary<string, int> idLevels);
}
