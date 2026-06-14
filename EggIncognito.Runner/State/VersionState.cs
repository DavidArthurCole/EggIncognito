using System.IO;

namespace EggIncognito.Runner.State;

// VersionState persists the last-seen game version so the loop is restart-safe.
public sealed class VersionState
{
    private readonly string _path;

    public VersionState(string path) => _path = path;

    // LastSeen returns the stored version, or empty if the file is absent.
    public string LastSeen() =>
        File.Exists(_path) ? File.ReadAllText(_path).Trim() : "";

    // Save writes the new last-seen version. Single writer, no locking needed.
    public void Save(string version) => File.WriteAllText(_path, version);
}
