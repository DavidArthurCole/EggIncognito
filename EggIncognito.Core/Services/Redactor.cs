// Redacts personally-identifying values from decoded endpoint and request JSON before it is written to
// disk. The EID is scrubbed separately by the caller; this handles everything else, replacing each
// sensitive value with a stable, one-way SHA256-derived token.

using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace EggIncognito.Services;

public static class Redactor
{
    // camelCase JSON field names, as emitted by Google.Protobuf JsonFormatter, whose string values are sensitive.
    private static readonly string[] SensitiveFields =
    [
        "transactionId", "originalTransactionId", "linkedTransactionId",
        "deviceId", "deviceName", "pushUserId",
        "gameServicesId", "gameServicesIdScoped", "code", "signature", "receipt",
        "advertisingId", "deviceAdId", "pushId",
        "coopIdentifier",
        "userName", "requestingUserName", "username", "alias",
    ];

    // Escape-aware: a JSON string value with an embedded \" is consumed whole instead of stopping early.
    private static readonly Regex FieldRegex = new(
        "\"(" + string.Join('|', SensitiveFields) + ")\":\\s*\"((?:[^\"\\\\]|\\\\.)+)\"",
        RegexOptions.Compiled);

    /// <summary>The camelCase JSON field names treated as sensitive. Exposed so the dashboard can blur
    /// the same keys in the live UI, in addition to redacting on write.</summary>
    public static IReadOnlyList<string> SensitiveFieldNames => SensitiveFields;

    /// <summary>Replace every sensitive field value with a stable redaction token. Called on every
    /// endpoint and request dump before write.</summary>
    public static string Redact(string json) =>
        FieldRegex.Replace(json, m => $"\"{m.Groups[1].Value}\": \"{Token(m.Groups[2].Value)}\"");

    // 12 hex chars of SHA256: collision-free in practice, reads as an obviously-fake placeholder.
    private static string Token(string value) =>
        "redacted-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..12].ToLowerInvariant();
}
