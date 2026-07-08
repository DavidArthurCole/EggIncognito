using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace EggIncognito.Core.Services.Devices;

// Pure cert-derivation helpers for the CA installers, factored out so they unit-test without a device.
// Both values are computed in-process from the DER cert (no openssl on the device needed).
public static class CaCertPrep
{
    // Android's system trust store names each cert `<subject_hash_old>.0`: OpenSSL's legacy hash, MD5
    // over the cert's raw DER subject, first 4 bytes read little-endian as 8 lowercase hex digits.
    // Matches `openssl x509 -subject_hash_old`.
    public static string AndroidSubjectHashOld(X509Certificate2 cert)
    {
        var md5 = MD5.HashData(cert.SubjectName.RawData);
        uint h = (uint)(md5[0] | md5[1] << 8 | md5[2] << 16 | md5[3] << 24);
        return h.ToString("x8");
    }

    // The PEM the Android system store expects. The framework only parses the PEM block, so no
    // human-readable text dump is emitted.
    public static string ToPem(X509Certificate2 cert)
    {
        var b64 = Convert.ToBase64String(cert.RawData);
        var sb = new System.Text.StringBuilder();
        sb.Append("-----BEGIN CERTIFICATE-----\n");
        for (var i = 0; i < b64.Length; i += 64)
            sb.Append(b64, i, Math.Min(64, b64.Length - i)).Append('\n');
        sb.Append("-----END CERTIFICATE-----\n");
        return sb.ToString();
    }

    // iOS TrustStore.sqlite3 `tsettings` primary key (iOS 16+ `sha256` column, SHA-256 of the full DER
    // cert). Lowercase hex; the installer wraps it X'...'.
    public static string IosCertSha256Hex(X509Certificate2 cert) =>
        Convert.ToHexString(SHA256.HashData(cert.RawData)).ToLowerInvariant();

    // SHA-1 fingerprint, for the older (iOS <=15) `sha1`-column schema.
    public static string IosCertSha1Hex(X509Certificate2 cert) =>
        Convert.ToHexString(SHA1.HashData(cert.RawData)).ToLowerInvariant();

    // The `subj` blob in the same row is the cert's DER-encoded subject name. Lowercase hex.
    public static string IosSubjectDerHex(X509Certificate2 cert) =>
        Convert.ToHexString(cert.SubjectName.RawData).ToLowerInvariant();

    // The full DER cert as a lowercase hex string, for the iOS TrustStore `data` blob (X'...') literal.
    public static string DerHex(X509Certificate2 cert) => Convert.ToHexString(cert.RawData).ToLowerInvariant();
}
