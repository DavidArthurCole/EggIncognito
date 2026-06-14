namespace EggIncognito.Runner.Extract;

// Default until a proven extraction recipe exists. Emits null rather than a guessed value, so the
// registry never stores a fabricated clientVersion.
public sealed class NullClientVersionReader : IClientVersionReader
{
    public string? Read(string apkPath) => null;
}
