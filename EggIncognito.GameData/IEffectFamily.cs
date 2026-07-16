namespace EggIncognito.GameData;

public interface IEffectFamily
{
    string Key { get; }
    EffectSchema? MetaSchema { get; }
    IReadOnlyList<Effect> Effects { get; }
    Effect? Find(string id);
}
