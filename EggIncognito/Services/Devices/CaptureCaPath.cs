using EggIncognito.Core.Services;

namespace EggIncognito.Services.Devices;

public static class CaptureCaPath {
    public static string Resolve(IConfiguration config) {
        string contentRoot = ContentRoot.Resolve(config["ContentRoot"]);
        string capturePath = config["CapturePath"] ?? Path.Combine(contentRoot, "captures");
        return config["CaPath"] ?? Path.Combine(capturePath, "eggincognito-ca.cer");
    }
}
