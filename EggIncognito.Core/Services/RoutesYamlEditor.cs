// Load-once / save-once editor for routes.yaml. Consolidates every yaml mutation the extractor
// performs. All edits operate on an in-memory line list and are flushed by a single Save().
// Hard rules, enforced here so callers cannot violate them:
//   - Never overwrite a concrete existing value. Only empty, "# NEEDS CAPTURE" placeholder, or literal
//     "AuthenticatedMessage" slots may be filled.
//   - Only ever emit line forms the two yaml consumers already accept, RouteGenerator and
//     RouteCatalog. A `- path:` line is only ever written inside `routes:`.

using System.Text;
using System.Text.RegularExpressions;

namespace EggIncognito.Services;

public sealed class RoutesYamlEditor
{
    private readonly string _path;
    private readonly List<string> _lines;
    private DateTime _loadedStampUtc;
    private bool _dirty;

    public bool Dirty => _dirty;

    public RoutesYamlEditor(string contentRoot)
    {
        _path = ContentRoot.RoutesYamlPath(contentRoot);
        // Split on \n; we re-join with \n on Save to preserve LF endings.
        _lines = File.ReadAllText(_path).Replace("\r\n", "\n").Split('\n').ToList();
        _loadedStampUtc = File.GetLastWriteTimeUtc(_path);
    }

    public void Save()
    {
        if (!_dirty) return;
        // Another writer touched the file after our load; saving would silently overwrite their
        // edits with this stale view. Abort - the caller reloads and re-applies.
        if (File.GetLastWriteTimeUtc(_path) != _loadedStampUtc)
            throw new IOException($"routes.yaml changed on disk since load; aborting save to avoid clobbering concurrent edits: {_path}");
        File.WriteAllText(_path, string.Join('\n', _lines), new UTF8Encoding(false));
        _dirty = false;
        _loadedStampUtc = File.GetLastWriteTimeUtc(_path);
    }

    public bool HasPath(string path) => FindRouteStart(path) >= 0;

    /// <summary>Fill request:/response: only when the current value is empty, placeholder, or literal
    /// AuthenticatedMessage. Returns true if it wrote. Never clobbers a concrete value.</summary>
    public bool SetFieldIfEmpty(string path, string key, string value)
    {
        var (start, end) = RouteBlock(path);
        if (start < 0) return false;

        var legacy = LegacyAlias(key);
        for (int k = start + 1; k < end; k++)
        {
            // Match either the new key or its legacy alias on this line.
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
        Insert(start + 1, $"{FieldIndent(start, end)}{key}: {value}");
        _dirty = true;
        return true;
    }

    /// <summary>Set requestWrapped:/responseWrapped: true. Inserts if absent; never flips an existing
    /// explicit flag. Returns true if it changed anything.</summary>
    public bool SetWrappedFlag(string path, string key)
    {
        var (start, end) = RouteBlock(path);
        if (start < 0) return false;

        for (int k = start + 1; k < end; k++)
        {
            var m = Regex.Match(_lines[k], @"^\s*" + Regex.Escape(key) + @":\s*([^#]*?)\s*(?:#.*)?$");
            if (m.Success) return false; // already set - leave it
        }
        Insert(start + 1, $"{FieldIndent(start, end)}{key}: true");
        _dirty = true;
        return true;
    }

    /// <summary>Map a captured path to its canonical route. If the path is not itself a known route but
    /// its parent one segment up is a `pathParam: true` route, the trailing segment is a path-parameter
    /// value - return the parent. Prevents the extractor from minting bogus path-param child routes.</summary>
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

    /// <summary>Append a brand-new route block into its namespace section. No-op if present or if the
    /// path is a path-parameter child of an existing route.</summary>
    public bool AddRoute(string path, string? request, bool requestWrapped, string response, bool responseWrapped)
    {
        if (HasPath(path)) return false;
        if (CanonicalPath(path) != path) return false; // path-param child, never add as new

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

    /// <summary>Remove a path from needs_capture. Keeps the sublist header even if it empties. Returns
    /// true if an item was removed.</summary>
    public bool RemoveFromNeedsCapture(string path)
    {
        int nc = _lines.FindIndex(l => l.StartsWith("needs_capture:", StringComparison.Ordinal));
        if (nc < 0) return false;
        int end = NextTopLevelKey(nc);

        for (int k = nc + 1; k < end; k++)
        {
            // List item form: `    - <path>` with optional trailing comment.
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

    // Marker comment for a route confirmed by capture to post no request body. Distinguishes a resolved
    // empty request from an unfilled placeholder.
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
            if (m.Groups[3].Value.Trim().Length > 0) return false; // concrete, never clobber
            _lines[k] = $"{m.Groups[1].Value}request:  {NoneMarker}";
            _dirty = true;
            return true;
        }
        Insert(start + 1, $"{FieldIndent(start, end)}request:  {NoneMarker}");
        _dirty = true;
        return true;
    }

    /// <summary>Is the route's request slot still unresolved? The explicit none-marker counts as
    /// resolved.</summary>
    public bool RequestUnresolved(string path)
    {
        var (start, end) = RouteBlock(path);
        if (start >= 0)
            for (int k = start + 1; k < end; k++)
                if (Regex.IsMatch(_lines[k], @"^\s*request:\s*" + Regex.Escape(NoneMarker) + @"\s*$"))
                    return false; // confirmed no body
        return FieldUnresolved(path, "request", "requestType");
    }

    public bool ResponseUnresolved(string path) => FieldUnresolved(path, "response", "responseType");

    private bool FieldUnresolved(string path, string key, string legacy)
    {
        var (start, end) = RouteBlock(path);
        if (start < 0) return true; // not present, so unresolved
        for (int k = start + 1; k < end; k++)
        {
            var m = Regex.Match(_lines[k], @"^\s*(" + Regex.Escape(key) + "|" + Regex.Escape(legacy) + @"):\s*([^#]*?)\s*(?:#.*)?$");
            if (m.Success)
            {
                var v = m.Groups[2].Value.Trim();
                return v.Length == 0 || v == "AuthenticatedMessage";
            }
        }
        return true; // key absent, so unresolved
    }

    private void Insert(int index, string line)
    {
        _lines.Insert(index, line);
    }

    // Indent for a route block's fields: copied from the first existing field line, else the
    // `- path:` line's indent plus two spaces. Never hardcoded, so off-standard files keep their
    // own indentation.
    private string FieldIndent(int start, int end)
    {
        for (int k = start + 1; k < end; k++)
        {
            var m = Regex.Match(_lines[k], @"^(\s+)\S");
            if (m.Success) return m.Groups[1].Value;
        }
        return Regex.Match(_lines[start], @"^\s*").Value + "  ";
    }

    private static string LegacyAlias(string key) => key switch
    {
        "request" => "requestType",
        "response" => "responseType",
        _ => key,
    };

    private int FindRouteStart(string path) =>
        _lines.FindIndex(l => Regex.IsMatch(l, @"^\s*-\s+path:\s+" + Regex.Escape(path) + @"\s*$"));

    // Returns [start, end) line range of a route block. end is the next `- path:` or top-level key.
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

    // Insertion point at the end of a namespace section, after its last route block and before the
    // blank line or next section comment. Falls back to end of the `routes:` section.
    private int SectionInsertPoint(string ns)
    {
        int routes = _lines.FindIndex(l => l.StartsWith("routes:", StringComparison.Ordinal));
        if (routes < 0) return -1;
        int routesEnd = NextTopLevelKey(routes);

        int comment = _lines.FindIndex(routes, l => l.Trim() == $"# {ns}/");
        if (comment < 0 || comment >= routesEnd)
            return routesEnd; // no section comment; append at end of routes

        // Walk to the next section comment or end of routes, then insert before the trailing blank.
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
