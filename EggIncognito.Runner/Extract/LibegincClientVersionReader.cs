using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Runner.Extract;

public sealed class LibegincClientVersionReader : IClientVersionReader {
    public string? Read(string apkPath, int? previousClientVersion) {
        try {
            var bytes = File.ReadAllBytes(apkPath);
            return LibegincClientVersion.Read(bytes)?.ToString();
        } catch {
            return null;
        }
    }
}
