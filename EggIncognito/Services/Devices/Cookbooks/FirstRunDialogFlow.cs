using EggIncognito.Core.Services.Devices;
using EggIncognito.Models.Devices;

namespace EggIncognito.Services.Devices.Cookbooks;

public static class FirstRunDialogFlow {
    public const string PlayDialogText = "Check that Google Play is enabled on your device";
    public const string DialogButtonId = "android:id/button1";
    public const string DialogButtonLabel = "Close";
    private const int Rounds = 3;

    public static IReadOnlyList<DeviceFlowStep> Build(GmsFirstRunConfig? gms = null) {
        var steps = new List<DeviceFlowStep>();
        for (int i = 0; i < Rounds; i++) {
            steps.Add(DeviceFlowSteps.WaitForText(
                $"{PlayDialogText} OR {DialogButtonLabel}", timeoutSeconds: i == 0 ? 6 : 2, pollSeconds: 1,
                required: false));
            steps.Add(DeviceFlowSteps.Tap(UiSelector.Id(DialogButtonId), required: false));
            steps.Add(DeviceFlowSteps.Tap(UiSelector.Text(DialogButtonLabel), required: false));
        }

        steps.Add(DeviceFlowSteps.WaitForTextGone(PlayDialogText, timeoutSeconds: 4, pollSeconds: 1, required: false));
        if (gms is not null) steps.AddRange(BuildGmsSteps(gms));
        return steps;
    }

    private static IEnumerable<DeviceFlowStep> BuildGmsSteps(GmsFirstRunConfig gms) {
        yield return DeviceFlowSteps.WaitForText(gms.TosText, timeoutSeconds: 4, pollSeconds: 1, required: false);
        yield return DeviceFlowSteps.Tap(UiSelector.Text(gms.AcceptLabel), required: false);
        yield return DeviceFlowSteps.WaitForText(
            gms.BackupPromptText, timeoutSeconds: 4, pollSeconds: 1, required: false);
        yield return DeviceFlowSteps.Tap(UiSelector.Text(gms.SkipBackupLabel), required: false);
        yield return DeviceFlowSteps.WaitForText(
            gms.SignInPromptText, timeoutSeconds: 4, pollSeconds: 1, required: false);
        yield return DeviceFlowSteps.Tap(UiSelector.Text(gms.SkipSignInLabel), required: false);
    }
}
