using System.Text.RegularExpressions;

namespace EggIncognito.Components.Capture;

// View preferences + redaction logic for the capture detail pane, ported from redaction.js + the
// outcome/format helpers of helpers.js. A plain mutable holder fanned down to the detail components by
// parameter; the page loads/persists it through the captureStore.js interop. Three redaction modes:
//   off    - raw json, nothing blurred
//   blur   - raw json, sensitive values blurred (CSS .blurred), revealed on click
//   redact - pre-redacted json with "redacted-xxxx" tokens
public sealed class CaptureViewState
{
    // Matches an EID anywhere in a string: path params, query values, etc.
    public static readonly Regex EidRe = new("EI\\d{10,}", RegexOptions.Compiled);

    // Field names whose values are blurred in blur mode, at any depth. Seeded with the EID fields; the
    // rest are added from /api/capture/sensitive-keys.
    public HashSet<string> SensitiveKeys { get; } = new(StringComparer.Ordinal) { "eiUserId", "userId" };

    public string RedactionMode { get; set; } = "blur";
    public bool ShowHeaders { get; set; }
    public bool AutoScroll { get; set; } = true;
    public bool CompareToKnown { get; set; }
    public string DefaultFormat { get; set; } = "json-tree";

    public bool IsBlurMode => RedactionMode == "blur";
    public bool IsRedactMode => RedactionMode == "redact";

    // Whether to render the unredacted header value: only in Off mode (mirrors body raw behavior).
    public bool ShowRawHeaders => RedactionMode == "off";

    // Pick which JSON string to render for a flow side. redact uses pre-redacted; off/blur use raw,
    // falling back to redacted if null. The per-value blur happens later in the tree.
    public string? PickJson(string? redacted, string? raw) =>
        RedactionMode == "redact" ? redacted : raw ?? redacted;

    // True when a value under keyName should be blurred: blur mode active and the field is sensitive.
    public bool IsSensitiveKey(string? keyName) =>
        RedactionMode == "blur" && keyName is not null && SensitiveKeys.Contains(keyName);

    public bool LooksLikeEid(string s) => EidRe.IsMatch(s);

    // Apply blur/redact to a single path/query param value. EID-looking values are redacted to
    // "redacted-eid" or blurred; others pass through. Returns (text, blur) - blur true means wrap the
    // span in a click-to-reveal blur. Ported from redactParamValue.
    public (string Text, bool Blur) RedactParamValue(string value)
    {
        if (RedactionMode == "redact" && EidRe.IsMatch(value)) return ("redacted-eid", false);
        return (value, RedactionMode == "blur" && EidRe.IsMatch(value));
    }

    // Tokenize a path on '/', so any EID-looking segment respects the current mode while the rest stays
    // raw. Returns the parts in order; Blur true means render that segment blurred. Ported from
    // renderRedactedPath.
    public IEnumerable<(string Text, bool Blur)> RenderRedactedPath(string path)
    {
        // Keep separators so the path renders exactly as-is.
        var parts = Regex.Split(path, "(/)");
        foreach (var part in parts)
        {
            if (part.Length == 0) continue;
            if (EidRe.IsMatch(part))
            {
                if (RedactionMode == "redact") { yield return ("redacted-eid", false); continue; }
                yield return (part, RedactionMode == "blur");
            }
            else
            {
                yield return (part, false);
            }
        }
    }

    // Collect the exact string values that should be blurred in text views: any value under a sensitive
    // key, plus any string that looks like an EID anywhere. Ported from collectSensitiveValues.
    public HashSet<string> CollectSensitiveValues(System.Text.Json.Nodes.JsonNode? value)
    {
        var outSet = new HashSet<string>(StringComparer.Ordinal);
        Visit(value, null, outSet);
        return outSet;
    }

    void Visit(System.Text.Json.Nodes.JsonNode? v, string? keyName, HashSet<string> outSet)
    {
        switch (v)
        {
            case null:
                return;
            case System.Text.Json.Nodes.JsonArray arr:
                foreach (var item in arr) Visit(item, keyName, outSet);
                return;
            case System.Text.Json.Nodes.JsonObject obj:
                foreach (var kv in obj) Visit(kv.Value, kv.Key, outSet);
                return;
            default:
                var jv = (System.Text.Json.Nodes.JsonValue)v;
                var s = jv.TryGetValue(out string? str) && str is not null ? str : jv.ToJsonString();
                var sensitive = (keyName is not null && SensitiveKeys.Contains(keyName)) || EidRe.IsMatch(s);
                if (sensitive) outSet.Add(s);
                return;
        }
    }
}

// Small shared formatting helpers, ported from helpers.js. Pure, used across the flow list + detail.
public static class CaptureHelpers
{
    public static string StatusClass(int status) => status switch
    {
        >= 200 and < 300 => "status-2xx",
        >= 300 and < 400 => "status-3xx",
        >= 500 => "status-5xx",
        _ => "status-4xx",
    };

    public static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.0} KB";
        return $"{bytes / (1024.0 * 1024.0):0.0} MB";
    }

    public sealed record OutcomeMeta(string Label, string Kind, string Desc);

    // Map an endpoint-write outcome string to a label/kind/desc. Null for empty/unknown.
    public static OutcomeMeta? Outcome(string? outcome) => outcome switch
    {
        "wrote" => new("wrote", "good", "New endpoint written to disk (none existed before)."),
        "upd" => new("upd", "good", "Updated - an empty/placeholder endpoint was filled in."),
        "diff" => new("diff", "warn", "Differs from the saved endpoint - staged for review, not overwritten."),
        "loss" => new("loss", "bad", "Could not decode into an endpoint (no proto type / unparseable) - nothing saved."),
        "same" => new("same", "same", "Identical to the saved endpoint - no change."),
        _ => null,
    };

    public static bool HasDiffCounts(string? outcome, int added, int removed) =>
        outcome == "diff" && (added > 0 || removed > 0);
}
