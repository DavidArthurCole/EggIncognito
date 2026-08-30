namespace EggIncognito.Models.Devices;

public sealed class GmsFirstRunConfig {
    public string TosText { get; set; } = "Terms of Service";
    public string AcceptLabel { get; set; } = "Accept";
    public string BackupPromptText { get; set; } = "Copy apps & data";
    public string SkipBackupLabel { get; set; } = "Skip";
    public string SignInPromptText { get; set; } = "Sign in";
    public string SkipSignInLabel { get; set; } = "Skip";
}
