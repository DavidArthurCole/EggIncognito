namespace EggIncognito.Runner.Extract;

// Reads the proto/API clientVersion (e.g. 72) from a pulled arm split, or null when not determinable.
// Null is a valid, registry-accepted value. previousClientVersion anchors the disambiguation: the value
// is a compiled-in constant among many small-int candidates, and clientVersion increments by 0-1 per
// build, so the reader picks the candidate in {prev, prev+1, prev+2}.
public interface IClientVersionReader
{
    string? Read(string apkPath, int? previousClientVersion);
}
