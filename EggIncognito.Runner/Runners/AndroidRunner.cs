using EggIdentity.Contract;
using EggIncognito.Core.Services.Devices;
using EggIncognito.Runner.Adb;
using EggIncognito.Runner.Extract;
using EggIncognito.Runner.State;

namespace EggIncognito.Runner.Runners;

public sealed class AndroidRunner(
    IAdbClient adb, IProtoExtractor proto, VersionState state, IClientVersionReader clientVersion,
    ClientVersionState cvState, string package, string apkStashDir,
    Action<NewVersionEvent> onNewVersion) : IDeviceRunner {
    public string Platform => "android";

    public RunOutcome RunOnce(bool force) {
        var dumpsys = adb.DumpsysPackage(package);
        var (appVersion, build) = DeviceParsing.AndroidVersion(dumpsys);
        if (string.IsNullOrEmpty(build))
            return new RunOutcome(false, null, null, "no versionCode in dumpsys");
        if (!force && build == state.LastSeen())
            return new RunOutcome(false, build, null, "build already seen");

        var apkPath = Path.Combine(apkStashDir, $"egginc-{build}.apk");
        adb.PullArmApk(package, apkPath);

        var extraction = proto.Extract(apkPath);
        var protoBytes = extraction.ProtoText;
        var protoSha = extraction.ProtoSha;

        var cv = clientVersion.Read(apkPath, cvState.Last());
        if (cv is not null && int.TryParse(cv, out var cvNum)) cvState.Save(cvNum);

        onNewVersion(new NewVersionEvent {
            Package = package,
            Version = appVersion ?? "",
            AppVersion = appVersion ?? "",
            Build = build,
            ClientVersion = cv,
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
