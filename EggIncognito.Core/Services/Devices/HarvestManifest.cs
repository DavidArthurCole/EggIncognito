namespace EggIncognito.Core.Services.Devices;

public sealed record HarvestEntry(string Name, string Kind, bool Supported = true, string? UnsupportedNote = null);

public sealed record HarvestItem(string Name, byte[] Bytes, string ContentType);

public sealed record HarvestBatch(IReadOnlyList<HarvestItem> Items, IReadOnlyList<string> Present, bool Authoritative) {
    public static HarvestBatch Empty { get; } = new([], [], false);
}

public static class HarvestEntries {
    public const string AppBinary = "app-binary";
    public const string AppPackage = "app-package";
    public const string Meshes = "meshes";
    public const string Textures = "textures";
    public const string PackageManifest = "package-manifest";

    public const string AndroidArmSplit = "arm-split.apk";
    public const string AndroidBaseSplit = "base.apk";
}
