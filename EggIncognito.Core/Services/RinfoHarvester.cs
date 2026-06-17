using System.Text.Json;

namespace EggIncognito.Services;

// Harvests the BasicRequestInfo (rinfo) fields out of a captured request's ALREADY-DECODED display JSON.
// Every live auxbrain request the client sends carries rinfo: { clientVersion, version, build, platform }.
// The capture pipeline decrypts + decodes the request to Google.Protobuf JSON upstream (FlowDecoder), so
// here we only read that JSON. This is the authoritative source for iOS clientVersion + the real build,
// which the static binary cannot give. NOTE: System.Text.Json is correct here because the input is plain
// display JSON, not the proto wire; the proto<->JSON boundary (JsonParser/JsonFormatter only) is upstream.
public static class RinfoHarvester
{
    public sealed record ObservedVersion(string Platform, string? Version, string? Build, int? ClientVersion);

    // Returns the rinfo fields, or null when the JSON has no usable rinfo. Never throws.
    public static ObservedVersion? TryHarvest(string? requestJson)
    {
        if (string.IsNullOrWhiteSpace(requestJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(requestJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            if (!TryGetProperty(doc.RootElement, "rinfo", out var rinfo) || rinfo.ValueKind != JsonValueKind.Object)
                return null;

            string? platform = TryGetProperty(rinfo, "platform", out var p) && p.ValueKind == JsonValueKind.String
                ? p.GetString()?.ToUpperInvariant() : null;
            string? version = TryGetProperty(rinfo, "version", out var v) && v.ValueKind == JsonValueKind.String
                ? NullIfEmpty(v.GetString()) : null;
            string? build = TryGetProperty(rinfo, "build", out var b) && b.ValueKind == JsonValueKind.String
                ? NullIfEmpty(b.GetString()) : null;
            int? clientVersion = ReadClientVersion(rinfo);

            // Nothing useful -> not an observation.
            if (platform is null && version is null && build is null && clientVersion is null) return null;
            return new ObservedVersion(platform ?? "", version, build, clientVersion);
        }
        catch
        {
            return null;
        }
    }

    private static int? ReadClientVersion(JsonElement rinfo)
    {
        if (!TryGetProperty(rinfo, "clientVersion", out var cv)) return null;
        return cv.ValueKind switch
        {
            JsonValueKind.Number when cv.TryGetInt32(out var n) => n,
            JsonValueKind.String when int.TryParse(cv.GetString(), out var n) => n,
            _ => null,
        };
    }

    // Google.Protobuf JSON is camelCase, but be tolerant of casing drift.
    private static bool TryGetProperty(JsonElement obj, string name, out JsonElement value)
    {
        foreach (var prop in obj.EnumerateObject())
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        value = default;
        return false;
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
