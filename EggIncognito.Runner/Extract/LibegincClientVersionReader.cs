using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Runner.Extract;

// Returns null when prev is unknown or no in-range candidate is found.
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
