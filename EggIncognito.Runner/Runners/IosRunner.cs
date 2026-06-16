using System.Security.Cryptography;
using EggIncognito.Core.Models;
using EggIncognito.Services.ProtoExtract;
using EggIncognito.Runner.State;

namespace EggIncognito.Runner.Runners;

// iOS path: the app ships protobuf with descriptor support, so a decrypted Mach-O embeds the
// FileDescriptorProto. MachoProtoExtractor carves it - that's the recipe the old stub was missing.
// What is NOT automated yet is pulling a fresh decrypted binary off a device (no iOS equivalent of adb +
// bagbak here), so this runner extracts from a binary already staged on disk (binaryPath). State is keyed
// on the binary's content hash since iOS gives us no versionCode. force ignores state. Stays testable
// without a device: point binaryPath at a decrypted Mach-O.
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
