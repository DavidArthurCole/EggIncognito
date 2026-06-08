// Redacts personally-identifying / sensitive values from decoded endpoint and request JSON
// before it is written to disk (and ultimately committed to a public repo). The EID is
// scrubbed separately by the caller; this handles everything else.
//
// Each sensitive value is replaced by a short SHA256-derived token. Redaction is STABLE
// (same input always yields the same token) so endpoints diff cleanly across re-seeds, and
// one-way (the original value cannot be recovered from the token).

using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace EggIncognito.Services;

public static class Redactor
{
    // JSON field names (camelCase, as emitted by Google.Protobuf JsonFormatter) whose string
    // values are sensitive. Grouped only for readability; all are treated the same way.
    private static readonly string[] SensitiveFields =
    [
        // transaction / receipt identifiers (real purchase data)
        "transactionId", "originalTransactionId", "linkedTransactionId",
        // device identifiers
        "deviceId", "deviceName", "pushUserId",
        // account / platform auth identifiers + the AuthenticatedMessage signature
        "gameServicesId", "gameServicesIdScoped", "code",
        // coop identity
        "coopIdentifier",
        // player-visible names / handles
        "userName", "requestingUserName", "alias",
    ];

    private static readonly Regex FieldRegex = new(
        "\"(" + string.Join('|', SensitiveFields) + ")\":\\s*\"([^\"]+)\"",
        RegexOptions.Compiled);

    /// <summary>The camelCase JSON field names treated as sensitive. Exposed so the dashboard can
    /// blur the same keys in the live UI (in addition to redacting on write).</summary>
    public static IReadOnlyList<string> SensitiveFieldNames => SensitiveFields;

    /// <summary>Replace every sensitive field value with a stable redaction token.
    /// Public for testing; called on every endpoint + request dump before write.</summary>
    public static string Redact(string json) =>
        FieldRegex.Replace(json, m => $"\"{m.Groups[1].Value}\": \"{Token(m.Groups[2].Value)}\"");

    // 12 hex chars of SHA256 - enough to stay collision-free in practice, short enough to
    // read as an obviously-fake placeholder.
    private static string Token(string value) =>
        "redacted-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..12].ToLowerInvariant();
}
