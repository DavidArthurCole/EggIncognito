using System.Globalization;
using System.Text.Json;

namespace EggIncognito.Core.Services;

public static class RinfoHarvester {
    public static ObservedVersion? TryHarvest(string? requestJson) {
        if (string.IsNullOrWhiteSpace(requestJson)) return null;
        try {
            using var doc = JsonDocument.Parse(requestJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            if (!TryGetProperty(doc.RootElement, "rinfo", out var rinfo) || rinfo.ValueKind != JsonValueKind.Object)
                return null;

            string? platform = TryGetProperty(rinfo, "platform", out var p) && p.ValueKind == JsonValueKind.String
                ? p.GetString()?.ToUpperInvariant()
                : null;
            string? version = TryGetProperty(rinfo, "version", out var v) && v.ValueKind == JsonValueKind.String
                ? NullIfEmpty(v.GetString())
                : null;
            string? build = TryGetProperty(rinfo, "build", out var b) && b.ValueKind == JsonValueKind.String
                ? NullIfEmpty(b.GetString())
                : null;
            int? clientVersion = ReadClientVersion(rinfo);

            return platform is null && version is null && build is null && clientVersion is null
                ? null
                : new ObservedVersion(platform ?? "", version, build, clientVersion);
        } catch {
            return null;
        }
    }

    private static int? ReadClientVersion(JsonElement rinfo) {
        return !TryGetProperty(rinfo, "clientVersion", out var cv)
            ? null
            : cv.ValueKind switch {
                JsonValueKind.Number when cv.TryGetInt32(out int n) => n,
                JsonValueKind.String when int.TryParse(cv.GetString(), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out int n) => n,
                _ => null
            };
    }

    private static bool TryGetProperty(JsonElement obj, string name, out JsonElement value) {
        foreach (var prop in obj.EnumerateObject()) {
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase)) {
                value = prop.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    public sealed record ObservedVersion(string Platform, string? Version, string? Build, int? ClientVersion);
}
