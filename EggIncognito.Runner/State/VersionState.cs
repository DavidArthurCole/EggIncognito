namespace EggIncognito.Runner.State;

// VersionState persists the last-seen game version so the loop is restart-safe.
public sealed class VersionState
{
    private readonly string _path;

    public VersionState(string path) => _path = path;

    public string LastSeen() =>
        File.Exists(_path) ? File.ReadAllText(_path).Trim() : "";

    // Single writer, no locking needed.
    public void Save(string version) => File.WriteAllText(_path, version);
}
