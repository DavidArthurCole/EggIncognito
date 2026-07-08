namespace EggIncognito.Runner.Extract;

// clientVersion increments 0-1 per build; previousClientVersion anchors the pick to {prev, prev+1, prev+2}.
public interface IClientVersionReader
{
    string? Read(string apkPath, int? previousClientVersion);
}
