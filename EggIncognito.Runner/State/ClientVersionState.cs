namespace EggIncognito.Runner.State;

// Persists the last extracted clientVersion so the disasm reader has an anchor across runs. Seeded once
// from PREV_CLIENT_VERSION env (bootstrap), then self-advances after each successful extract.
public sealed class ClientVersionState(string path, int? seed)
{
    public int? Last()
    {
        if (File.Exists(path) && int.TryParse(File.ReadAllText(path).Trim(), out var v)) return v;
        return seed;
    }

    public void Save(int value) => File.WriteAllText(path, value.ToString());
}
