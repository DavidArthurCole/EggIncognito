using EggIdentity.Contract;
using EggIncognito.Core;
using EggIncognito.Runner.State;
using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Runner.Runners;

public sealed class IosRunner(
    string binaryPath, VersionState state, string package,
    Action<NewVersionEvent> onNewVersion) : IDeviceRunner {
    public string Platform => "ios";

    public RunOutcome RunOnce(bool force) {
        if (!File.Exists(binaryPath))
            return new RunOutcome(false, null, null, $"no staged ios binary at {binaryPath}");

        var macho = File.ReadAllBytes(binaryPath);
        var build = Hashes.Sha256HexShort(macho, 16);
        if (!force && build == state.LastSeen())
            return new RunOutcome(false, build, null, "binary already seen");

        var result = MachoProtoExtractor.Extract(macho);
        if (!result.Ok || result.Proto is null)
            return new RunOutcome(false, build, null, result.Diagnostics);

        var protoBytes = System.Text.Encoding.UTF8.GetBytes(result.Proto);
        var protoSha = result.ProtoSha ?? Hashes.Sha256Hex(protoBytes);

        onNewVersion(new NewVersionEvent {
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
