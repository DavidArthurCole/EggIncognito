namespace EggIncognito.Runner.Extract;

// Fallback for hosts without the disasm toolchain. Emits null rather than a guessed value, so the
// registry never stores a fabricated clientVersion.
public sealed class NullClientVersionReader : IClientVersionReader
{
    public string? Read(string apkPath, int? previousClientVersion) => null;
}
