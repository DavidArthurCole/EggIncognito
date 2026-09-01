namespace EggIncognito.Core.Services.Devices;

public static class ImageBuildStates {
    public const string Queued = "queued";
    public const string Downloading = "downloading";
    public const string Building = "building";
    public const string Ready = "ready";
    public const string Failed = "failed";

    public static bool IsTerminal(string? state) => state is Ready or Failed;
}

public sealed record ImageBuildSpec(
    string AndroidVersion,
    bool Gapps,
    bool Magisk,
    bool Ndk,
    string? BaseImage = null,
    string? Tag = null) {
    public string ResolvedBaseImage =>
        string.IsNullOrWhiteSpace(BaseImage) ? $"redroid/redroid:{AndroidVersion}-latest" : BaseImage;

    public string ResolvedTag => string.IsNullOrWhiteSpace(Tag) ? AssembleTag() : Tag;

    public string AssembleTag() {
        var tokens = new List<string> { AndroidVersion };
        if (Gapps) tokens.Add("gapps");
        if (Ndk) tokens.Add("ndk");
        if (Magisk) tokens.Add("magisk");
        return "redroid/redroid:" + string.Join('_', tokens);
    }
}
