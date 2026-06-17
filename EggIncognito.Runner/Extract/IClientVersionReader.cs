namespace EggIncognito.Runner.Extract;

// Reads the proto clientVersion (e.g. 72) from a pulled arm split, or null when not determinable.
// previousClientVersion anchors disambiguation: the value is a compiled-in constant among many
// small-int candidates; clientVersion increments 0-1 per build, so pick from {prev, prev+1, prev+2}.
public interface IClientVersionReader
{
    string? Read(string apkPath, int? previousClientVersion);
}
