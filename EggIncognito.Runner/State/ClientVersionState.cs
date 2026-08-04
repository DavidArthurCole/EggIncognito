namespace EggIncognito.Runner.State;

public sealed class ClientVersionState(string path, int? seed) {
    public int? Last() {
        if (File.Exists(path) && int.TryParse(File.ReadAllText(path).Trim(), out var v)) return v;
        return seed;
    }

    public void Save(int value) => File.WriteAllText(path, value.ToString());
}
