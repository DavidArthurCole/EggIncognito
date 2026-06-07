// EggIncognito/Services/RoutesYamlEditor.cs
//
// Load-once / save-once editor for routes.yaml. Consolidates every yaml mutation the
// seeder performs (the old AddToRoutesYaml + SetRequestTypeInYaml did multiple full-file
// rewrites with brittle regex passes). All edits operate on an in-memory line list and are
// flushed by a single Save().
//
// Hard rules (enforced here so callers cannot violate them):
//   - Never overwrite a CONCRETE existing value (only empty / "# NEEDS CAPTURE" placeholder
//     / literal "AuthenticatedMessage" slots may be filled).
//   - Only ever emit line forms the three yaml consumers already accept (Generator,
//     RouteCatalog, CodeGen). A `- path:` line is only ever written inside `routes:`.

using System.Text;
using System.Text.RegularExpressions;

namespace EggIncognito.Services;

public sealed class RoutesYamlEditor
{
    private readonly string _path;
    private readonly List<string> _lines;
    private bool _dirty;

    public bool Dirty => _dirty;

    public RoutesYamlEditor(string repoRoot)
    {
        _path = Path.Combine(repoRoot, "EggIncognito", "RouteMap", "routes.yaml");
        // Split on \n, keeping content; we re-join with \n on Save to preserve LF endings.
        _lines = File.ReadAllText(_path).Replace("\r\n", "\n").Split('\n').ToList();
    }

    public void Save()
    {
        if (!_dirty) return;
        File.WriteAllText(_path, string.Join('\n', _lines), new UTF8Encoding(false));
        _dirty = false;
    }

    public bool HasPath(string path) => FindRouteStart(path) >= 0;

    /// <summary>Fill request:/response: only when the current value is empty / placeholder /
    /// literal AuthenticatedMessage. Returns true if it wrote. Never clobbers a concrete value.</summary>
    public bool SetFieldIfEmpty(string path, string key, string value)
    {
        var (start, end) = RouteBlock(path);
        if (start < 0) return false;

        var legacy = LegacyAlias(key);
        for (int k = start + 1; k < end; k++)
        {
            // Match either the new key (request/response) or its legacy alias on this line.
            var m = Regex.Match(_lines[k], @"^(\s*)(" + Regex.Escape(key) + "|" + Regex.Escape(legacy) + @"):\s*([^#]*?)\s*(?:#.*)?$");
            if (!m.Success) continue;

            var existing = m.Groups[3].Value.Trim();
            if (existing.Length > 0 && existing != "AuthenticatedMessage")
                return false; // concrete value present - never clobber
            _lines[k] = $"{m.Groups[1].Value}{key}: {value}";
            _dirty = true;
            return true;
        }

        // Key absent entirely: insert in canonical order after the path line.
        Insert(start + 1, $"    {key}: {value}");
        _dirty = true;
        return true;
    }

    /// <summary>Set requestWrapped:/responseWrapped: true. Inserts if absent; never flips an
    /// existing explicit flag. Returns true if it changed anything.</summary>
    public bool SetWrappedFlag(string path, string key)
    {
        var (start, end) = RouteBlock(path);
        if (start < 0) return false;

        for (int k = start + 1; k < end; k++)
        {
            var m = Regex.Match(_lines[k], @"^\s*" + Regex.Escape(key) + @":\s*([^#]*?)\s*(?:#.*)?$");
            if (m.Success) return false; // already set (either value) - leave it
        }
        Insert(start + 1, $"    {key}: true");
        _dirty = true;
        return true;
    }

    /// <summary>Map a captured path to its canonical route. If the path is not itself a
    /// known route but its parent (one segment up) is a `pathParam: true` route, the
    /// trailing segment is a path-parameter VALUE (e.g. a contract id) - return the parent.
    /// Prevents the seeder from minting bogus routes like get_contract_evaluation/pumpkin-pie.</summary>
    public string CanonicalPath(string path)
    {
        if (HasPath(path)) return path;
        var slash = path.LastIndexOf('/');
        if (slash <= 0) return path;
        var parent = path[..slash];
        return HasPathParam(parent) ? parent : path;
    }

    private bool HasPathParam(string path)
    {
        var (start, end) = RouteBlock(path);
        if (start < 0) return false;
        for (int k = start + 1; k < end; k++)
            if (Regex.IsMatch(_lines[k], @"^\s*pathParam:\s*true\s*(?:#.*)?$")) return true;
        return false;
    }

    /// <summary>Append a brand-new route block into its namespace section. No-op if present
    /// or if the path is a path-parameter child of an existing route.</summary>
    public bool AddRoute(string path, string? request, bool requestWrapped, string response, bool responseWrapped)
    {
        if (HasPath(path)) return false;
        if (CanonicalPath(path) != path) return false; // path-param child - never add as new

        var ns = path.Split('/')[0];
        int insertAt = SectionInsertPoint(ns);
        if (insertAt < 0) return false;

        var block = new List<string> { $"  - path: {path}" };
        block.Add(string.IsNullOrEmpty(request)
            ? "    request:  # TODO review - request type not detected"
            : $"    request: {request}");
        if (requestWrapped) block.Add("    requestWrapped: true");
        block.Add($"    response: {response}");
        if (responseWrapped) block.Add("    responseWrapped: true");

        _lines.InsertRange(insertAt, block);
        _dirty = true;
        return true;
    }

    /// <summary>Remove a path from needs_capture (request_unknown / fully_unknown). Keeps the
    /// sublist header even if it empties. Returns true if an item was removed.</summary>
    public bool RemoveFromNeedsCapture(string path)
    {
        int nc = _lines.FindIndex(l => l.StartsWith("needs_capture:", StringComparison.Ordinal));
        if (nc < 0) return false;
        int end = NextTopLevelKey(nc);

        for (int k = nc + 1; k < end; k++)
        {
            // List item form: `    - <path>` optionally with a trailing comment.
            var m = Regex.Match(_lines[k], @"^\s*-\s+(\S+)\s*(?:#.*)?$");
            if (m.Success && m.Groups[1].Value == path)
            {
                _lines.RemoveAt(k);
                _dirty = true;
                return true;
            }
        }
        return false;
    }

    // Marker comment for a route confirmed (from capture) to post no request body.
    // Distinguishes a RESOLVED empty request from an unfilled placeholder.
    private const string NoneMarker = "# none - empty body";

    /// <summary>Record that the route posts no request proto. Returns true if it changed.</summary>
    public bool MarkRequestNone(string path)
    {
        var (start, end) = RouteBlock(path);
        if (start < 0) return false;
        for (int k = start + 1; k < end; k++)
        {
            var m = Regex.Match(_lines[k], @"^(\s*)(request|requestType):\s*([^#]*?)\s*(?:#.*)?$");
            if (!m.Success) continue;
            if (m.Groups[3].Value.Trim().Length > 0) return false; // concrete - never clobber
            _lines[k] = $"{m.Groups[1].Value}request:  {NoneMarker}";
            _dirty = true;
            return true;
        }
        Insert(start + 1, $"    request:  {NoneMarker}");
        _dirty = true;
        return true;
    }

    /// <summary>Is the route's request slot still unresolved (empty/placeholder/AM)?
    /// The explicit none-marker counts as RESOLVED.</summary>
    public bool RequestUnresolved(string path)
    {
        var (start, end) = RouteBlock(path);
        if (start >= 0)
            for (int k = start + 1; k < end; k++)
                if (Regex.IsMatch(_lines[k], @"^\s*request:\s*" + Regex.Escape(NoneMarker) + @"\s*$"))
                    return false; // confirmed no-body
        return FieldUnresolved(path, "request", "requestType");
    }

    public bool ResponseUnresolved(string path) => FieldUnresolved(path, "response", "responseType");

    private bool FieldUnresolved(string path, string key, string legacy)
    {
        var (start, end) = RouteBlock(path);
        if (start < 0) return true; // not present => unresolved
        for (int k = start + 1; k < end; k++)
        {
            var m = Regex.Match(_lines[k], @"^\s*(" + Regex.Escape(key) + "|" + Regex.Escape(legacy) + @"):\s*([^#]*?)\s*(?:#.*)?$");
            if (m.Success)
            {
                var v = m.Groups[2].Value.Trim();
                return v.Length == 0 || v == "AuthenticatedMessage";
            }
        }
        return true; // key absent => unresolved
    }

    // --- line-model helpers ---

    private void Insert(int index, string line)
    {
        _lines.Insert(index, line);
    }

    private static string LegacyAlias(string key) => key switch
    {
        "request" => "requestType",
        "response" => "responseType",
        _ => key,
    };

    private int FindRouteStart(string path) =>
        _lines.FindIndex(l => Regex.IsMatch(l, @"^\s*-\s+path:\s+" + Regex.Escape(path) + @"\s*$"));

    // Returns [start, end) line range of a route block (end = next `- path:` / top-level key).
    private (int start, int end) RouteBlock(string path)
    {
        int start = FindRouteStart(path);
        if (start < 0) return (-1, -1);
        int end = _lines.Count;
        for (int k = start + 1; k < _lines.Count; k++)
        {
            if (Regex.IsMatch(_lines[k], @"^\s*-\s+path:") || Regex.IsMatch(_lines[k], @"^\w"))
            { end = k; break; }
        }
        return (start, end);
    }

    private int NextTopLevelKey(int from)
    {
        for (int k = from + 1; k < _lines.Count; k++)
            if (Regex.IsMatch(_lines[k], @"^\w[\w_]*:")) return k;
        return _lines.Count;
    }

    // Insertion point at the END of a namespace section (after its last route block, before
    // the blank line / next section comment). Falls back to end of `routes:` section.
    private int SectionInsertPoint(string ns)
    {
        int routes = _lines.FindIndex(l => l.StartsWith("routes:", StringComparison.Ordinal));
        if (routes < 0) return -1;
        int routesEnd = NextTopLevelKey(routes);

        int comment = _lines.FindIndex(routes, l => l.Trim() == $"# {ns}/");
        if (comment < 0 || comment >= routesEnd)
            return routesEnd; // no section comment; append at end of routes

        // Walk to the next section comment or end of routes; insert before trailing blank.
        int at = routesEnd;
        for (int k = comment + 1; k < routesEnd; k++)
        {
            if (_lines[k].TrimStart().StartsWith("# ") && _lines[k].Trim().EndsWith('/'))
            { at = k; break; }
        }
        while (at > comment && _lines[at - 1].Trim().Length == 0) at--; // skip blank lines
        return at;
    }
}
