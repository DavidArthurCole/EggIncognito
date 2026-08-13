namespace EggIncognito.Core.Services.Devices;

public enum DeviceAssetKind {
    Mesh,
    Texture
}

public static class DeviceAssetKinds {
    public const string Binary = "binary";
    public const string Package = "package";
    public const string Mesh = "mesh";
    public const string Icon = "icon";
    public const string Config = "config";
    public const string Manifest = "manifest";

    public const string AnyPlatform = "any";
}
