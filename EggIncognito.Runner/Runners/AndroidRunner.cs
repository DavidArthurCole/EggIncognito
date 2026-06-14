using System.Security.Cryptography;
using EggIncognito.Core.Models;
using EggIncognito.Runner.Adb;
using EggIncognito.Runner.Extract;
using EggIncognito.Runner.State;

namespace EggIncognito.Runner.Runners;

// The proven android path: dumpsys for appVersion+build, pull the arm split, extract+clean the proto,
// hash it, emit a NewVersionEvent. State is keyed on build (versionCode), the unique row key. force
// ignores state. Timing and posting are the caller's concern, so this stays testable without a device.
public sealed class AndroidRunner(
    IAdbClient adb, IProtoExtractor proto, VersionState state, IClientVersionReader clientVersion,
    string package, string apkStashDir, Action<NewVersionEvent> onNewVersion) : IDeviceRunner
{
    public string Platform => "android";

    public RunOutcome RunOnce(bool force)
    {
        var dumpsys = adb.DumpsysPackage(package);
        var appVersion = AdbClient.ParseVersionName(dumpsys);
        var build = AdbClient.ParseVersionCode(dumpsys);
        if (build == "")
            return new RunOutcome(false, null, null, "no versionCode in dumpsys");
        if (!force && build == state.LastSeen())
            return new RunOutcome(false, build, null, "build already seen");

        var apkPath = Path.Combine(apkStashDir, $"egginc-{build}.apk");
        adb.PullArmApk(package, apkPath);

        var protoBytes = proto.Extract(apkPath);
        var protoSha = Convert.ToHexString(SHA256.HashData(protoBytes)).ToLowerInvariant();

        onNewVersion(new NewVersionEvent
        {
            Package = package,
            Version = appVersion,
            AppVersion = appVersion,
            Build = build,
            ClientVersion = clientVersion.Read(apkPath),
            ApkRef = apkPath,
            ProtoSha = protoSha,
            Platform = Platform,
            ProtoTextB64 = Convert.ToBase64String(protoBytes),
            DetectedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
        });
        state.Save(build);
        return new RunOutcome(true, build, protoSha, "emitted");
    }
}
