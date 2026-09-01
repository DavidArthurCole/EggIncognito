namespace EggIncognito.Models.Devices;

public sealed record ImageBuildRequest(
    string? AndroidVersion, bool Gapps, bool Magisk, bool Ndk, string? BaseImage);
