using System.Text;

namespace EggIncognito.Core.Services.Devices;

public static class IntegritySeed {
    public const string ServiceName = "egi-seed";
    public const string ServiceProp = "init.svc." + ServiceName;
    public const string SeedDir = "/system/etc/init/egi";
    public const string SeedScript = SeedDir + "/seed.sh";
    public const string ModulesDir = SeedDir + "/modules";
    public const string RcFile = "/system/etc/init/egi-seed.rc";
    public const string AdbKeysFile = SeedDir + "/adb_keys";
    public const string StateFile = "/data/local/tmp/egi-seed.state";
    public const string LogFile = "/data/local/tmp/egi-seed.log";
    public const string MarkerDir = "/data/adb/egi";
    public const string Marker = MarkerDir + "/seeded";
    public const string StateInstalling = "installing";
    public const string StateDone = "done";
    public const string StateFailed = "failed";
    public const string ServiceRunning = "running";
    public const string ServiceStopped = "stopped";
    public const string ImageMarker = "seeded-image";
    public const string PendingModuleDir = "/data/adb/modules_update/playintegrityfix";
    public const int MagiskWaitSeconds = 90;
    public const int BootWaitSeconds = 300;
    private const string ServicePrefix = "svc=";
    private const string LogPrefix = "log=";
    private const string MagiskBinDir = "/data/adb/magisk";
    private const string MagiskSysDir = "/system/etc/init/magisk";

    public const string ProbeCommand =
        "cat " + StateFile + " 2>/dev/null; [ -f " + SeedScript + " ] && echo " + ImageMarker + "; "
        + "echo \"" + ServicePrefix + "$(getprop " + ServiceProp + ")\"; "
        + "echo \"" + LogPrefix + "$(tail -n 1 " + LogFile + " 2>/dev/null)\"";

    public const string Rc =
        "service " + ServiceName + " /system/bin/sh " + SeedScript + "\n"
        + "    user root\n"
        + "    group root\n"
        + "    seclabel u:r:su:s0\n"
        + "    disabled\n"
        + "    oneshot\n"
        + "\n"
        + "on boot\n"
        + "    start " + ServiceName + "\n";

    private const string DeviceAdbDir = "/data/misc/adb";
    private const string DeviceAdbKeys = DeviceAdbDir + "/adb_keys";

    public static SeedProbe Parse(string stdout) {
        bool seededImage = false;
        string? state = null;
        string? service = null;
        string? lastLog = null;
        foreach (string raw in stdout.Split('\n')) {
            string line = raw.Trim();
            if (line == ImageMarker) seededImage = true;
            else if (line.StartsWith(ServicePrefix, StringComparison.Ordinal)) service = Blank(line[ServicePrefix.Length..]);
            else if (line.StartsWith(LogPrefix, StringComparison.Ordinal)) lastLog = Blank(line[LogPrefix.Length..]);
            else if (state is null && line is StateInstalling or StateDone or StateFailed) state = line;
        }

        return new SeedProbe(seededImage, state, service, lastLog);
    }

    private static string? Blank(string value) => value.Trim().Length == 0 ? null : value.Trim();

    public static string Script(IReadOnlyList<string> moduleFileNames) {
        var sb = new StringBuilder();
        sb.Append("#!/system/bin/sh\n");
        sb.Append("mkdir -p " + MarkerDir + "\n");
        sb.Append("if [ -f " + Marker + " ]; then\n");
        AppendState(sb, "    ", StateDone);
        sb.Append("    exit 0\n");
        sb.Append("fi\n");
        AppendState(sb, "", StateInstalling);
        sb.Append(": > " + LogFile + "\n");
        sb.Append("chmod 644 " + LogFile + "\n");
        sb.Append("exec >> " + LogFile + " 2>&1\n");
        sb.Append("fail() {\n");
        sb.Append("    echo \"failed: $1\"\n");
        AppendState(sb, "    ", StateFailed);
        sb.Append("    exit 1\n");
        sb.Append("}\n");
        sb.Append("step() {\n");
        sb.Append("    echo \"step: $1\"\n");
        sb.Append("}\n");
        AppendWait(sb, "boot", "[ \"$(getprop sys.boot_completed)\" = 1 ]", BootWaitSeconds, "sys.boot_completed never reached 1");
        sb.Append("MAGISK=/sbin/magisk\n");
        sb.Append("[ -x \"$MAGISK\" ] || MAGISK=" + MagiskSysDir + "/magisk\n");
        AppendWait(sb, "magisk", "\"$MAGISK\" -v >/dev/null 2>&1", MagiskWaitSeconds, "magisk daemon never answered $MAGISK -v");
        sb.Append("step \"magisk $(\"$MAGISK\" -v) at $MAGISK\"\n");
        sb.Append("if [ ! -f " + MagiskBinDir + "/util_functions.sh ] || [ ! -x " + MagiskBinDir + "/busybox ]; then\n");
        sb.Append("    step \"seeding " + MagiskBinDir + " from " + MagiskSysDir + "\"\n");
        sb.Append("    mkdir -p " + MagiskBinDir + "\n");
        sb.Append("    cp -f " + MagiskSysDir + "/* " + MagiskBinDir + "/\n");
        sb.Append("    rm -f " + MagiskBinDir + "/magisk.apk\n");
        sb.Append("    chmod 755 " + MagiskBinDir + "/*\n");
        sb.Append("    chcon u:object_r:magisk_file:s0 " + MagiskBinDir + " " + MagiskBinDir + "/* 2>/dev/null\n");
        sb.Append("fi\n");
        sb.Append("[ -f " + MagiskBinDir + "/util_functions.sh ] || fail \"" + MagiskBinDir + "/util_functions.sh missing; magisk --install-module cannot run\"\n");
        sb.Append("step \"magisk db: zygisk off, su for uid 2000\"\n");
        sb.Append("\"$MAGISK\" --sqlite \"REPLACE INTO settings (key,value) VALUES('zygisk',0)\"\n");
        sb.Append("\"$MAGISK\" --sqlite \"REPLACE INTO policies (uid,policy,until,logging,notification) VALUES(2000,2,0,1,1)\"\n");
        sb.Append("if [ -s " + AdbKeysFile + " ]; then\n");
        sb.Append("    step \"authorizing host adb key\"\n");
        sb.Append("    mkdir -p " + DeviceAdbDir + "\n");
        sb.Append("    touch " + DeviceAdbKeys + "\n");
        sb.Append("    while IFS= read -r key || [ -n \"$key\" ]; do\n");
        sb.Append("        [ -n \"$key\" ] || continue\n");
        sb.Append("        grep -qxF \"$key\" " + DeviceAdbKeys + " || echo \"$key\" >> " + DeviceAdbKeys + "\n");
        sb.Append("    done < " + AdbKeysFile + "\n");
        sb.Append("    chown 1000:2000 " + DeviceAdbDir + " " + DeviceAdbKeys + "\n");
        sb.Append("    chmod 750 " + DeviceAdbDir + "\n");
        sb.Append("    chmod 640 " + DeviceAdbKeys + "\n");
        sb.Append("else\n");
        sb.Append("    step \"no host adb key baked; adbd will reject the host after the identity props flip\"\n");
        sb.Append("fi\n");
        sb.Append("step \"pre-placing identity and keybox\"\n");
        sb.Append("mkdir -p " + IntegrityChain.PifModuleDir + " " + IntegrityChain.TrickyStoreDir + "\n");
        sb.Append("cp -f " + SeedDir + "/" + IntegrityChain.PifPropFileName + " " + IntegrityChain.PifProp + "\n");
        sb.Append("cp -f " + SeedDir + "/" + IntegrityChain.KeyboxFileName + " " + IntegrityChain.TrickyKeybox + "\n");
        sb.Append("chmod 600 " + IntegrityChain.TrickyKeybox + "\n");
        foreach (string name in moduleFileNames) {
            sb.Append("step \"installing " + name + "\"\n");
            sb.Append("\"$MAGISK\" --install-module " + ModulesDir + "/" + name + " || fail \"install of " + name + " failed\"\n");
        }

        sb.Append("step \"applying identity to the module dirs\"\n");
        sb.Append(IntegrityChain.ApplyScript(SeedDir, PendingModuleDir) + "\n");
        sb.Append("step \"clearing play store and gsf\"\n");
        sb.Append("pm clear " + IntegrityChain.PlayStorePackage + " >/dev/null 2>&1\n");
        sb.Append("pm clear " + IntegrityChain.GsfPackage + " >/dev/null 2>&1\n");
        sb.Append("touch " + Marker + "\n");
        AppendState(sb, "", StateDone);
        sb.Append("step \"seeded, rebooting\"\n");
        sb.Append("sync\n");
        sb.Append("sleep 2\n");
        sb.Append("/system/bin/reboot\n");
        return sb.ToString();
    }

    private static void AppendWait(StringBuilder sb, string what, string condition, int seconds, string failure) {
        sb.Append("step \"waiting for " + what + "\"\n");
        sb.Append("n=0\n");
        sb.Append("until " + condition + "; do\n");
        sb.Append("    n=$((n + 1))\n");
        sb.Append("    [ \"$n\" -lt " + seconds + " ] || fail \"" + failure + " within " + seconds + "s\"\n");
        sb.Append("    sleep 1\n");
        sb.Append("done\n");
    }

    private static void AppendState(StringBuilder sb, string indent, string state) {
        sb.Append(indent + "echo " + state + " > " + StateFile + "\n");
        sb.Append(indent + "chmod 644 " + StateFile + "\n");
    }
}
