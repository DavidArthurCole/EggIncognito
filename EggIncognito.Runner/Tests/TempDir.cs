namespace EggIncognito.Runner.Tests;

public sealed class TempDir : IDisposable {
    public TempDir() {
        Directory.CreateDirectory(Path);
    }

    public string Path { get; } =
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "egi-test-" + Guid.NewGuid().ToString("N"));

    public string Combine(params string[] parts) =>
        System.IO.Path.Combine([Path, .. parts]);

    public string CreateSubdir() {
        string d = Combine(Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    public void Write(string name, string content) =>
        File.WriteAllText(Combine(name), content);

    public void Dispose() {
        try {
            Directory.Delete(Path, true);
        } catch {
        }
    }
}
