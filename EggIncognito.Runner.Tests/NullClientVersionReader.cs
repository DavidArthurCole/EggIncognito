namespace EggIncognito.Runner.Extract;

// Test double for IClientVersionReader; not wired in Program.cs.
public sealed class NullClientVersionReader : IClientVersionReader
{
    public string? Read(string apkPath, int? previousClientVersion) => null;
}
