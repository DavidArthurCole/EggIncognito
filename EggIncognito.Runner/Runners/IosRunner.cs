using System.Security.Cryptography;
using EggIncognito.Services.ProtoExtract;
using EggIncognito.Runner.State;
using SyncKit.Contract;

namespace EggIncognito.Runner.Runners;

// binaryPath must be pre-staged on disk; no automated device pull. State is keyed on content hash since iOS has no versionCode.
public sealed class IosRunner(
    string binaryPath, VersionState state, string package,
    Action<NewVersionEvent> onNewVersion) : IDeviceRunner
{
    public string Platform => "ios";

    public RunOutcome RunOnce(bool force)
    {
        if (!File.Exists(binaryPath))
            return new RunOutcome(false, null, null, $"no staged ios binary at {binaryPath}");

        var macho = File.ReadAllBytes(binaryPath);
        var build = Convert.ToHexString(SHA256.HashData(macho))[..16].ToLowerInvariant();
        if (!force && build == state.LastSeen())
            return new RunOutcome(false, build, null, "binary already seen");

        var result = MachoProtoExtractor.Extract(macho);
        if (!result.Ok || result.Proto is null)
            return new RunOutcome(false, build, null, result.Diagnostics);

        var protoBytes = System.Text.Encoding.UTF8.GetBytes(result.Proto);
        var protoSha = Convert.ToHexString(SHA256.HashData(protoBytes)).ToLowerInvariant();

        onNewVersion(new NewVersionEvent
        {
            Package = package,
            Version = "",
            AppVersion = "",
            Build = build,
            ClientVersion = null,
            ApkRef = binaryPath,
            ProtoSha = protoSha,
            Platform = Platform,
            ProtoTextB64 = Convert.ToBase64String(protoBytes),
            DetectedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
        });
        state.Save(build);
        return new RunOutcome(true, build, protoSha, "emitted");
    }
}
