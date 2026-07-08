using System.Text.Json;

namespace EggIncognito.Services;

// Harvests the BasicRequestInfo (rinfo: clientVersion/version/build/platform) fields out of a captured
// request's already-decoded display JSON. System.Text.Json is correct here since the input is plain
// display JSON, not the proto wire; the proto<->JSON boundary is upstream in FlowDecoder.
public static class RinfoHarvester
{
    public sealed record ObservedVersion(string Platform, string? Version, string? Build, int? ClientVersion);

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
