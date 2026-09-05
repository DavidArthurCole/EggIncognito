using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Tests.Devices;

public class IntegritySeedTests {
    private static readonly string[] Modules = ["zygisksu.zip", "teesim.zip", "playintegrityfix.zip"];

    [Fact]
    public void Script_DisablesZygiskBeforeInstallingModules() {
        string script = IntegritySeed.Script(Modules);

        int zygisk = script.IndexOf("VALUES('zygisk',0)", StringComparison.Ordinal);
        int install = script.IndexOf("--install-module", StringComparison.Ordinal);
        Assert.True(zygisk >= 0);
        Assert.True(install > zygisk);
    }

    [Fact]
    public void Script_InstallsModulesInOrder() {
        string script = IntegritySeed.Script(Modules);

        int first = script.IndexOf("--install-module " + IntegritySeed.ModulesDir + "/zygisksu.zip", StringComparison.Ordinal);
        int second = script.IndexOf("--install-module " + IntegritySeed.ModulesDir + "/teesim.zip", StringComparison.Ordinal);
        int third = script.IndexOf("--install-module " + IntegritySeed.ModulesDir + "/playintegrityfix.zip", StringComparison.Ordinal);
        Assert.True(first >= 0);
        Assert.True(second > first);
        Assert.True(third > second);
    }

    [Fact]
    public void Script_WritesBothStateMarkersAndTouchesSeededMarker() {
        string script = IntegritySeed.Script(Modules);

        Assert.Contains("echo " + IntegritySeed.StateInstalling + " > " + IntegritySeed.StateFile, script, StringComparison.Ordinal);
        Assert.Contains("echo " + IntegritySeed.StateDone + " > " + IntegritySeed.StateFile, script, StringComparison.Ordinal);
        Assert.Contains("echo " + IntegritySeed.StateFailed + " > " + IntegritySeed.StateFile, script, StringComparison.Ordinal);
        Assert.Contains("touch " + IntegritySeed.Marker + "\n", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_StartsWithShebangAndEndsWithReboot() {
        string script = IntegritySeed.Script(Modules);

        Assert.StartsWith("#!/system/bin/sh\n", script, StringComparison.Ordinal);
        Assert.EndsWith("/system/bin/reboot\n", script, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_AppliesChainFromSeedDirIntoPendingModule() {
        string script = IntegritySeed.Script(Modules);

        Assert.Contains(IntegrityChain.ApplyScript(IntegritySeed.SeedDir, IntegritySeed.PendingModuleDir), script, StringComparison.Ordinal);
        Assert.Contains("pm clear " + IntegrityChain.PlayStorePackage, script, StringComparison.Ordinal);
        Assert.Contains("pm clear " + IntegrityChain.GsfPackage, script, StringComparison.Ordinal);
    }

    [Fact]
    public void Rc_RunsSeedScriptOnBootCompleted() {
        Assert.StartsWith("on property:sys.boot_completed=1\n", IntegritySeed.Rc, StringComparison.Ordinal);
        Assert.Contains("/system/bin/sh " + IntegritySeed.SeedScript, IntegritySeed.Rc, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_ReadsStateAndImageMarker() {
        Assert.Equal(new SeedProbe(true, "installing"), IntegritySeed.Parse("installing\nseeded-image\n"));
        Assert.Equal(new SeedProbe(true, "done"), IntegritySeed.Parse("done\r\nseeded-image\r\n"));
        Assert.Equal(new SeedProbe(false, "failed"), IntegritySeed.Parse("failed\n"));
        Assert.Equal(new SeedProbe(false, null), IntegritySeed.Parse(""));
    }
}
