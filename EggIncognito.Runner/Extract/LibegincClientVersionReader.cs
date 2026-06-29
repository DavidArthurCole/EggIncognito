using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Runner.Extract;

// Reads the Egg Inc clientVersion from an arm-split APK in-process (pure C#), replacing the old
// client_version.py capstone shell-out. Returns null when prev is unknown or no in-range candidate is
// found. The arm split bytes are read from the path the caller already staged.
public sealed class LibegincClientVersionReader : IClientVersionReader
{
    public string? Read(string apkPath, int? previousClientVersion)
    {
        if (previousClientVersion is null) return null;
        try
        {
            var bytes = File.ReadAllBytes(apkPath);
            var cv = LibegincClientVersion.Read(bytes, previousClientVersion);
            return cv?.ToString();
        }
        catch
        {
            return null;
        }
    }
}
