namespace EggIncognito.Runner.Extract;

public sealed class NullClientVersionReader : IClientVersionReader {
    public string? Read(string apkPath, int? previousClientVersion) => null;
}
