using EggIncognito.Core.Services.Devices;
using EggIncognito.Models.Devices;
using EggIncognito.Services.Devices;

namespace EggIncognito.Tests.Devices;

public class AndroidRecertFlowTests {
    private static DeviceRecertConfig Config() => new() {
        KsuWebUiPackage = "me.weishu.kernelsu",
        MagiskPackage = "com.topjohnwu.magisk",
        PlayPackage = "com.android.vending",
        IntegrityHubLabel = "Integrity Hub",
        RepairModeLabel = "Repair Mode",
        RepairCompleteText = "repair complete OR check play integrity now",
        MagiskModulesLabel = "Modules",
        IntegrityBoxLabel = "Integrity box",
        MagiskActionLabel = "Action",
        RepairTimeoutSeconds = 180,
        MagiskActionWaitSeconds = 30
    };

    [Fact]
    public void BuildPrimary_SelectorConfigured_ProducesExpectedOrderedSteps() {
        var c = Config();
        c.PowerButtonResourceId = "id.power";

        var steps = AndroidRecertFlow.BuildPrimary(c);

        Assert.Collection(steps,
            s => Assert.Equal(DeviceKey.Wake, s.Key),
            s => Assert.Equal(DeviceKey.DismissKeyguard, s.Key),
            s => {
                Assert.Equal(DeviceFlowStepKind.LaunchApp, s.Kind);
                Assert.Equal("me.weishu.kernelsu", s.AppRef);
            },
            s => {
                Assert.Equal(DeviceFlowStepKind.WaitForText, s.Kind);
                Assert.Equal("Integrity Hub", s.Text);
                Assert.Equal(30, s.TimeoutSeconds);
            },
            s => {
                Assert.Equal(DeviceFlowStepKind.Tap, s.Kind);
                Assert.Equal(UiSelector.Text("Repair Mode"), s.Selector);
            },
            s => {
                Assert.Equal(DeviceFlowStepKind.WaitForSelector, s.Kind);
                Assert.Equal(UiSelector.Id("id.power"), s.Selector);
            },
            s => {
                Assert.Equal(DeviceFlowStepKind.Tap, s.Kind);
                Assert.Equal(UiSelector.Id("id.power"), s.Selector);
            },
            s => {
                Assert.Equal(DeviceFlowStepKind.WaitForText, s.Kind);
                Assert.Equal("repair complete OR check play integrity now", s.Text);
                Assert.Equal(180, s.TimeoutSeconds);
                Assert.Equal(5, s.PollSeconds);
            },
            s => {
                Assert.Equal(DeviceFlowStepKind.Screenshot, s.Kind);
                Assert.Equal("after-repair", s.Label);
            });
    }

    [Fact]
    public void BuildPrimary_PointConfigured_TapsThePoint() {
        var c = Config();
        c.PowerButtonX = 500;
        c.PowerButtonY = 900;

        var steps = AndroidRecertFlow.BuildPrimary(c);

        var powerStep = Assert.Single(steps, s => s.Kind == DeviceFlowStepKind.TapPoint);
        Assert.Equal(500, powerStep.X);
        Assert.Equal(900, powerStep.Y);
    }

    [Fact]
    public void BuildPrimary_SelectorPreferredOverPoint_WhenBothConfigured() {
        var c = Config();
        c.PowerButtonResourceId = "id.power";
        c.PowerButtonX = 500;
        c.PowerButtonY = 900;

        var steps = AndroidRecertFlow.BuildPrimary(c);

        Assert.DoesNotContain(steps, s => s.Kind == DeviceFlowStepKind.TapPoint);
        Assert.Contains(steps, s => s.Kind == DeviceFlowStepKind.WaitForSelector);
    }

    [Fact]
    public void BuildPrimary_DescConfigured_UsesContentDescSelector() {
        var c = Config();
        c.PowerButtonDesc = "Power";

        var steps = AndroidRecertFlow.BuildPrimary(c);

        Assert.Contains(steps, s => s.Kind == DeviceFlowStepKind.Tap && s.Selector == UiSelector.Desc("Power"));
    }

    [Fact]
    public void BuildPrimary_NeitherPowerButtonConfigured_EmitsAFailingAssertTextWithAClearNote() {
        var c = Config();

        var steps = AndroidRecertFlow.BuildPrimary(c);

        var fail = Assert.Single(steps, s => s.Kind == DeviceFlowStepKind.AssertText);
        Assert.Equal("recert: no PowerButton selector/point configured", fail.Text);
    }

    [Fact]
    public void BuildPrimary_ExpiryResourceIdConfigured_InsertsReadFieldBeforeRepairModeTap() {
        var c = Config();
        c.PowerButtonX = 1;
        c.PowerButtonY = 1;
        c.ExpiryFieldResourceId = "id.expiry";

        var list = AndroidRecertFlow.BuildPrimary(c).ToList();

        int readAt = list.FindIndex(s => s.Kind == DeviceFlowStepKind.ReadField);
        int tapAt = list.FindIndex(s => s.Kind == DeviceFlowStepKind.Tap);
        Assert.True(readAt >= 0 && readAt < tapAt);
        Assert.Equal("expiry", list[readAt].FieldName);
        Assert.Equal(UiSelector.Id("id.expiry"), list[readAt].Selector);
    }

    [Fact]
    public void BuildPrimary_ExpiryTextConfigured_UsesTextSelector() {
        var c = Config();
        c.PowerButtonX = 1;
        c.PowerButtonY = 1;
        c.ExpiryFieldText = "Expiry";

        var steps = AndroidRecertFlow.BuildPrimary(c);

        var read = Assert.Single(steps, s => s.Kind == DeviceFlowStepKind.ReadField);
        Assert.Equal(UiSelector.Text("Expiry"), read.Selector);
    }

    [Fact]
    public void BuildPrimary_NeitherExpirySelectorConfigured_NoReadFieldStep() {
        var c = Config();
        c.PowerButtonX = 1;
        c.PowerButtonY = 1;

        var steps = AndroidRecertFlow.BuildPrimary(c);

        Assert.DoesNotContain(steps, s => s.Kind == DeviceFlowStepKind.ReadField);
    }

    [Fact]
    public void BuildFallback_ProducesExpectedShape() {
        var c = Config();
        c.MagiskCloseResourceId = "id.close";

        var steps = AndroidRecertFlow.BuildFallback(c);

        Assert.Collection(steps,
            s => {
                Assert.Equal(DeviceFlowStepKind.LaunchApp, s.Kind);
                Assert.Equal("com.topjohnwu.magisk", s.AppRef);
            },
            s => {
                Assert.Equal(DeviceFlowStepKind.Tap, s.Kind);
                Assert.Equal(UiSelector.Text("Modules"), s.Selector);
            },
            s => {
                Assert.Equal(DeviceFlowStepKind.Tap, s.Kind);
                Assert.Equal(UiSelector.Text("Integrity box"), s.Selector);
            },
            s => {
                Assert.Equal(DeviceFlowStepKind.Tap, s.Kind);
                Assert.Equal(UiSelector.Text("Action"), s.Selector);
            },
            s => {
                Assert.Equal(DeviceFlowStepKind.WaitForText, s.Kind);
                Assert.False(s.Required);
            },
            s => {
                Assert.Equal(DeviceFlowStepKind.Sleep, s.Kind);
                Assert.Equal(30, s.TimeoutSeconds);
            },
            s => {
                Assert.Equal(DeviceFlowStepKind.Tap, s.Kind);
                Assert.Equal(UiSelector.Id("id.close"), s.Selector);
                Assert.False(s.Required);
            },
            s => {
                Assert.Equal(DeviceFlowStepKind.Screenshot, s.Kind);
                Assert.Equal("after-magisk", s.Label);
            });
    }

    [Fact]
    public void BuildFallback_ClosePointConfigured_TapsThePoint() {
        var c = Config();
        c.MagiskCloseX = 10;
        c.MagiskCloseY = 20;

        var steps = AndroidRecertFlow.BuildFallback(c);

        var close = Assert.Single(steps, s => s.Kind == DeviceFlowStepKind.TapPoint);
        Assert.Equal(10, close.X);
        Assert.Equal(20, close.Y);
    }

    [Fact]
    public void BuildFallback_NeitherCloseConfigured_NoCloseStep() {
        var c = Config();

        var steps = AndroidRecertFlow.BuildFallback(c);

        Assert.Equal(7, steps.Count);
        Assert.DoesNotContain(steps, s => s.Kind == DeviceFlowStepKind.TapPoint);
        Assert.Equal(3, steps.Count(s => s.Kind == DeviceFlowStepKind.Tap));
    }

    [Fact]
    public void BuildVerify_VerifyCertFalse_Empty() {
        var c = Config();
        c.VerifyCert = false;
        c.ProfileDesc = "Profile";
        c.SettingsLabel = "Settings";
        c.AboutLabel = "About";

        Assert.Empty(AndroidRecertFlow.BuildVerify(c));
    }

    [Fact]
    public void BuildVerify_VerifyCertTrueButSelectorsMissing_Empty() {
        var c = Config();
        c.VerifyCert = true;

        Assert.Empty(AndroidRecertFlow.BuildVerify(c));
    }

    [Fact]
    public void BuildVerify_FullyConfigured_ProducesStepsAllOptional() {
        var c = Config();
        c.VerifyCert = true;
        c.ProfileDesc = "Profile";
        c.SettingsLabel = "Settings";
        c.AboutLabel = "About";
        c.PlayProtectCertifiedText = "Device is certified";

        var steps = AndroidRecertFlow.BuildVerify(c);

        Assert.Equal("com.android.vending", steps[0].AppRef);
        Assert.All(steps, s => Assert.False(s.Required));
        var assert = Assert.Single(steps, s => s.Kind == DeviceFlowStepKind.AssertText);
        Assert.Equal("Device is certified", assert.Text);
    }
}
