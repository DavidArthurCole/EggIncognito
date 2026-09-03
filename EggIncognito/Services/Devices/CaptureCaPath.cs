using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using EggIncognito.Core.Services;
using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices;

public static class CaptureCaPath {
    public static string Resolve(IConfiguration config) {
        string contentRoot = ContentRoot.Resolve(config["ContentRoot"]);
        string capturePath = config["CapturePath"] ?? Path.Combine(contentRoot, "captures");
        return config["CaPath"] ?? Path.Combine(capturePath, "eggincognito-ca.cer");
    }

    public static string? AndroidTrustFile(IConfiguration config) {
        string path = Resolve(config);
        if (!File.Exists(path)) return null;

        try {
            using var cert = X509CertificateLoader.LoadCertificateFromFile(path);
            return CaCertPrep.AndroidSubjectHashOld(cert) + ".0";
        } catch (CryptographicException) {
            return null;
        } catch (IOException) {
            return null;
        }
    }
}
