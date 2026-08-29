using EggIncognito.Runner.Extract;

namespace EggIncognito.Runner.Tests;

public sealed class NullClientVersionReader : IClientVersionReader {
    public string? Read(string apkPath, int? previousClientVersion) => null;
}
