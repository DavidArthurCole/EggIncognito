using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace EggIncognito.Core.Services.Devices;

public static class CaCertPrep {
    public static string AndroidSubjectHashOld(X509Certificate2 cert) {
#pragma warning disable CA5351
        byte[] md5 = MD5.HashData(cert.SubjectName.RawData);
#pragma warning restore CA5351
        uint h = (uint)(md5[0] | (md5[1] << 8) | (md5[2] << 16) | (md5[3] << 24));
        return h.ToString("x8", CultureInfo.InvariantCulture);
    }


    public static string ToPem(X509Certificate2 cert) {
        string b64 = Convert.ToBase64String(cert.RawData);
        var sb = new StringBuilder();
        sb.Append("-----BEGIN CERTIFICATE-----\n");
        for (int i = 0; i < b64.Length; i += 64)
            sb.Append(b64, i, Math.Min(64, b64.Length - i)).Append('\n');
        sb.Append("-----END CERTIFICATE-----\n");
        return sb.ToString();
    }


    public static string IosCertSha256Hex(X509Certificate2 cert) => Hashes.Sha256Hex(cert.RawData);


#pragma warning disable CA5350
    public static string IosCertSha1Hex(X509Certificate2 cert) =>
        Convert.ToHexString(SHA1.HashData(cert.RawData)).ToLowerInvariant();
#pragma warning restore CA5350


    public static string IosSubjectDerHex(X509Certificate2 cert) =>
        Convert.ToHexString(cert.SubjectName.RawData).ToLowerInvariant();


    public static string DerHex(X509Certificate2 cert) => Convert.ToHexString(cert.RawData).ToLowerInvariant();
}
