namespace EggIncognito.Runner.Extract;

// Fallback for hosts without the disasm toolchain. Returns null rather than a guessed clientVersion.
public sealed class NullClientVersionReader : IClientVersionReader
{
    public string? Read(string apkPath, int? previousClientVersion) => null;
}
