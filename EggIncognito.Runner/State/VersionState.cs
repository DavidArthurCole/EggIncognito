namespace EggIncognito.Runner.State;

public sealed class VersionState {
    private readonly string _path;

    public VersionState(string path) => _path = path;

    public string LastSeen() =>
        File.Exists(_path) ? File.ReadAllText(_path).Trim() : "";

    public void Save(string version) => File.WriteAllText(_path, version);
}
