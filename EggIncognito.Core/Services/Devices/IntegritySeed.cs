using System.Text;

namespace EggIncognito.Core.Services.Devices;

public static class IntegritySeed {
    public const string SeedDir = "/system/etc/init/egi";
    public const string SeedScript = SeedDir + "/seed.sh";
    public const string ModulesDir = SeedDir + "/modules";
    public const string RcFile = "/system/etc/init/egi-seed.rc";
    public const string AdbKeysFile = SeedDir + "/adb_keys";
    public const string StateFile = "/data/local/tmp/egi-seed.state";
    public const string MarkerDir = "/data/adb/egi";
    public const string Marker = MarkerDir + "/seeded";
    public const string LogFile = MarkerDir + "/seed.log";
    public const string StateInstalling = "installing";
    public const string StateDone = "done";
    public const string StateFailed = "failed";
    public const string ImageMarker = "seeded-image";
    public const string PendingModuleDir = "/data/adb/modules_update/playintegrityfix";
    public const int MagiskWaitSeconds = 90;

    public const string ProbeCommand =
        "cat " + StateFile + " 2>/dev/null; [ -f " + SeedScript + " ] && echo " + ImageMarker;

    public const string Rc =
        "on property:sys.boot_completed=1\n"
        + "    exec_background u:r:su:s0 root root -- /system/bin/sh " + SeedScript + "\n";

    private const string DeviceAdbDir = "/data/misc/adb";
    private const string DeviceAdbKeys = DeviceAdbDir + "/adb_keys";

    public static SeedProbe Parse(string stdout) {
        bool seededImage = false;
        string? state = null;
        foreach (string raw in stdout.Split('\n')) {
            string line = raw.Trim();
            if (line == ImageMarker) seededImage = true;
            else if (state is null && line is StateInstalling or StateDone or StateFailed) state = line;
        }

        return new SeedProbe(seededImage, state);
    }

    public static string Script(IReadOnlyList<string> moduleFileNames) {
        var sb = new StringBuilder();
        sb.Append("#!/system/bin/sh\n");
        sb.Append("MAGISK=/sbin/magisk\n");
        sb.Append("[ -x \"$MAGISK\" ] || MAGISK=/system/etc/init/magisk/magisk\n");
        sb.Append("mkdir -p " + MarkerDir + "\n");
        sb.Append("if [ -f " + Marker + " ]; then\n");
        AppendState(sb, "    ", StateDone);
        sb.Append("    exit 0\n");
        sb.Append("fi\n");
        AppendState(sb, "", StateInstalling);
        sb.Append("exec >> " + LogFile + " 2>&1\n");
        sb.Append("n=0\n");
        sb.Append("until \"$MAGISK\" -v >/dev/null 2>&1; do\n");
        sb.Append("    n=$((n + 1))\n");
        sb.Append("    if [ \"$n\" -ge " + MagiskWaitSeconds + " ]; then\n");
        AppendState(sb, "        ", StateFailed);
        sb.Append("        exit 1\n");
        sb.Append("    fi\n");
        sb.Append("    sleep 1\n");
        sb.Append("done\n");
        sb.Append("\"$MAGISK\" --sqlite \"REPLACE INTO settings (key,value) VALUES('zygisk',0)\"\n");
        sb.Append("\"$MAGISK\" --sqlite \"REPLACE INTO policies (uid,policy,until,logging,notification) VALUES(2000,2,0,1,1)\"\n");
        sb.Append("if [ -s " + AdbKeysFile + " ]; then\n");
        sb.Append("    mkdir -p " + DeviceAdbDir + "\n");
        sb.Append("    touch " + DeviceAdbKeys + "\n");
        sb.Append("    while IFS= read -r key || [ -n \"$key\" ]; do\n");
        sb.Append("        [ -n \"$key\" ] || continue\n");
        sb.Append("        grep -qxF \"$key\" " + DeviceAdbKeys + " || echo \"$key\" >> " + DeviceAdbKeys + "\n");
        sb.Append("    done < " + AdbKeysFile + "\n");
        sb.Append("    chown 1000:2000 " + DeviceAdbDir + " " + DeviceAdbKeys + "\n");
        sb.Append("    chmod 750 " + DeviceAdbDir + "\n");
        sb.Append("    chmod 640 " + DeviceAdbKeys + "\n");
        sb.Append("fi\n");
        sb.Append("mkdir -p " + IntegrityChain.PifModuleDir + " " + IntegrityChain.TrickyStoreDir + "\n");
        sb.Append("cp -f " + SeedDir + "/" + IntegrityChain.PifPropFileName + " " + IntegrityChain.PifProp + "\n");
        sb.Append("cp -f " + SeedDir + "/" + IntegrityChain.KeyboxFileName + " " + IntegrityChain.TrickyKeybox + "\n");
        sb.Append("chmod 600 " + IntegrityChain.TrickyKeybox + "\n");
        foreach (string name in moduleFileNames) {
            sb.Append("if ! \"$MAGISK\" --install-module " + ModulesDir + "/" + name + "; then\n");
            AppendState(sb, "    ", StateFailed);
            sb.Append("    exit 1\n");
            sb.Append("fi\n");
        }

        sb.Append(IntegrityChain.ApplyScript(SeedDir, PendingModuleDir) + "\n");
        sb.Append("pm clear " + IntegrityChain.PlayStorePackage + " >/dev/null 2>&1\n");
        sb.Append("pm clear " + IntegrityChain.GsfPackage + " >/dev/null 2>&1\n");
        sb.Append("touch " + Marker + "\n");
        AppendState(sb, "", StateDone);
        sb.Append("sync\n");
        sb.Append("sleep 2\n");
        sb.Append("/system/bin/reboot\n");
        return sb.ToString();
    }

    private static void AppendState(StringBuilder sb, string indent, string state) {
        sb.Append(indent + "echo " + state + " > " + StateFile + "\n");
        sb.Append(indent + "chmod 644 " + StateFile + "\n");
    }
}
