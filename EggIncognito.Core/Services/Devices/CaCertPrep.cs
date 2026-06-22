using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace EggIncognito.Core.Services.Devices;

// Pure cert-derivation helpers for the CA installers, factored out so they unit-test without a device.
// Both values are computed in-process from the DER cert (no openssl on the device needed).
public static class CaCertPrep
{
    // Android's system trust store names each cert `<subject_hash_old>.0`, where subject_hash_old is
    // OpenSSL's legacy hash: MD5 over the cert's raw DER subject, first 4 bytes read little-endian, as
    // 8 lowercase hex digits. cert.SubjectName.RawData IS that DER subject sequence, so we hash it directly
    // (matching `openssl x509 -subject_hash_old`). This is the filename the framework looks up by.
    public static string AndroidSubjectHashOld(X509Certificate2 cert)
    {
        var md5 = MD5.HashData(cert.SubjectName.RawData);
        uint h = (uint)(md5[0] | md5[1] << 8 | md5[2] << 16 | md5[3] << 24);
        return h.ToString("x8");
    }

    // The PEM the Android system store expects: the cert in PEM, followed by the human-readable text dump
    // is NOT required by the framework (it only parses the PEM block), so we emit just the base64 PEM.
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

    // iOS TrustStore.sqlite3 `tsettings` primary key. Legacy iOS (<=15) used a `sha1` column = SHA-1 of the
    // full DER cert; iOS 16+ changed the schema to a `sha256` column = SHA-256 of the full DER cert. We key on
    // sha256 (the current schema). Lowercase hex; the installer wraps it X'...'.
    public static string IosCertSha256Hex(X509Certificate2 cert) =>
        Convert.ToHexString(SHA256.HashData(cert.RawData)).ToLowerInvariant();

    // Legacy SHA-1 fingerprint, kept for the old (iOS <=15) `sha1`-column schema if a device still uses it.
    public static string IosCertSha1Hex(X509Certificate2 cert) =>
        Convert.ToHexString(SHA1.HashData(cert.RawData)).ToLowerInvariant();

    // The `subj` blob in the same row is the cert's DER-encoded subject name. Lowercase hex.
    public static string IosSubjectDerHex(X509Certificate2 cert) =>
        Convert.ToHexString(cert.SubjectName.RawData).ToLowerInvariant();

    // The full DER cert as a lowercase hex string, for the iOS TrustStore `data` blob (X'...') literal.
    public static string DerHex(X509Certificate2 cert) => Convert.ToHexString(cert.RawData).ToLowerInvariant();
}
