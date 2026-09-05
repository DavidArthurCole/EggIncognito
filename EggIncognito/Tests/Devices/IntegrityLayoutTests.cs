using EggIncognito.Core.Services.Devices;
using EggIncognito.Services.Devices;

namespace EggIncognito.Tests.Devices;

public class IntegrityLayoutTests {
    private static IntegrityBundle Bundle() {
        var profile = new PifProfile("Google", "Pixel 6", "google", "oriole_beta", "oriole", "CANARY",
            "ZP11.260717.006", "16004061", "2026-08-05", PifProfile.LegacyInitialSdkInt, null, null);
        var modules = new List<IntegrityModuleAsset> {
            new(new IntegrityModuleSpec("zygisk", "a/b", null, "v1", null, true), "zygisksu", "v1", [1]),
            new(new IntegrityModuleSpec("tee", "a/c", null, "v4", null, false), "teesim", "v4", [2]),
            new(new IntegrityModuleSpec("integrity-box", "a/d", null, "v41", null, true), "playintegrityfix", "v41", [3])
        };
        return new IntegrityBundle(true, null, profile, PifProp.Render(profile), "<AndroidAttestation/>",
            "operator:x", ["abc"], "clean", "2026-08-05", modules, []);
    }

    [Fact]
    public void Plan_NumbersModulesInConfigOrder_AndMarksSeedExec() {
        var files = IntegrityLayout.Plan(Bundle(), "QAAA host", "1a2b3c4d", "-----BEGIN CERTIFICATE-----\nx\n-----END CERTIFICATE-----\n");

        var modules = files.Where(f => f.RelativePath.StartsWith("system/etc/init/egi/modules/", StringComparison.Ordinal))
            .Select(f => Path.GetFileName(f.RelativePath)).ToList();
        Assert.Equal(new[] { "01-zygisk.zip", "02-tee.zip", "03-integrity-box.zip" }, modules);

        var seed = files.Single(f => f.RelativePath == "system/etc/init/egi/seed.sh");
        Assert.True(seed.Exec);
        Assert.Contains(files, f => f.RelativePath == "system/etc/init/egi-seed.rc" && !f.Exec);
        Assert.Contains(files, f => f.RelativePath == "system/etc/init/egi/adb_keys");
        Assert.Contains(files, f => f.RelativePath == "system/etc/security/cacerts/1a2b3c4d.0");
        Assert.Contains(files, f => f.RelativePath == "system/etc/init/egi/custom.pif.prop");
        Assert.Contains(files, f => f.RelativePath == "system/etc/init/egi/keybox.xml");
        Assert.Contains(files, f => f.RelativePath == "system/etc/init/egi/target.txt");
        Assert.Contains(files, f => f.RelativePath == "system/etc/init/egi/security_patch.txt");
        Assert.All(files, f => Assert.False(f.RelativePath.StartsWith('/')));
    }

    [Fact]
    public void Plan_OmitsAdbKeyAndCa_WhenAbsent() {
        var files = IntegrityLayout.Plan(Bundle(), null, null, null);

        Assert.DoesNotContain(files, f => f.RelativePath.EndsWith("/adb_keys", StringComparison.Ordinal));
        Assert.DoesNotContain(files, f => f.RelativePath.StartsWith("system/etc/security/", StringComparison.Ordinal));
    }

    [Fact]
    public void Plan_RefusesFailedBundle() {
        var failed = new IntegrityBundle(false, "nope", null, null, null, null, [], null, null, [], []);

        var ex = Assert.Throws<InvalidOperationException>(() => IntegrityLayout.Plan(failed, null, null, null));
        Assert.Equal("nope", ex.Message);
    }
}
