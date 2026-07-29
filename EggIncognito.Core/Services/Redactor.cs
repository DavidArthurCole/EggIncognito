using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace EggIncognito.Services;

public static partial class Redactor {
    [GeneratedRegex(@"EI(?!0{15}\d)\d{16}", RegexOptions.Compiled)]
    private static partial Regex EidPattern();

    private static readonly string[] SensitiveFields = [
        "transactionId", "originalTransactionId", "linkedTransactionId",
        "deviceId", "deviceName", "pushUserId",
        "gameServicesId", "gameServicesIdScoped", "code", "signature", "receipt",
        "advertisingId", "deviceAdId", "pushId",
        "coopIdentifier",
        "userName", "requestingUserName", "username", "alias"
    ];


    private static readonly Regex FieldRegex = new(
        "\"(" + string.Join('|', SensitiveFields) + ")\":\\s*\"((?:[^\"\\\\]|\\\\.)+)\"",
        RegexOptions.Compiled);


    public static IReadOnlyList<string> SensitiveFieldNames => SensitiveFields;


    public static string Redact(string json) {
        string byField = FieldRegex.Replace(json, m => $"\"{m.Groups[1].Value}\": \"{Token(m.Groups[2].Value)}\"");
        return EidPattern().Replace(byField, m => "EI-" + Token(m.Value));
    }


    private static string Token(string value) =>
        "redacted-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..12].ToLowerInvariant();
}
