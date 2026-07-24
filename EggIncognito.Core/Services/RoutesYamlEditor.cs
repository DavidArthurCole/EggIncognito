

using System.Text;
using System.Text.RegularExpressions;

namespace EggIncognito.Services;

public sealed partial class RoutesYamlEditor {
    private readonly string _path;
    private readonly List<string> _lines;
    private DateTime _loadedStampUtc;

    public bool Dirty { get; private set; }

    public RoutesYamlEditor(string contentRoot) {
        _path = ContentRoot.RoutesYamlPath(contentRoot);
        _lines = [.. File.ReadAllText(_path).Replace("\r\n", "\n").Split('\n')];
        _loadedStampUtc = File.GetLastWriteTimeUtc(_path);
    }

    public void Save() {
        if (!Dirty) return;

        if (File.GetLastWriteTimeUtc(_path) != _loadedStampUtc)
            throw new IOException($"routes.yaml changed on disk since load; aborting save to avoid clobbering concurrent edits: {_path}");
        File.WriteAllText(_path, string.Join('\n', _lines), new UTF8Encoding(false));
        Dirty = false;
        _loadedStampUtc = File.GetLastWriteTimeUtc(_path);
    }

    public bool HasPath(string path) => FindRouteStart(path) >= 0;



    public bool SetFieldIfEmpty(string path, string key, string value) {
        var (start, end) = RouteBlock(path);
        if (start < 0) return false;

        var legacy = LegacyAlias(key);
        for (int k = start + 1; k < end; k++) {
            var m = Regex.Match(_lines[k], @"^(\s*)(" + Regex.Escape(key) + "|" + Regex.Escape(legacy) + @"):\s*([^#]*?)\s*(?:#.*)?$");
            if (!m.Success) continue;

            var existing = m.Groups[3].Value.Trim();
            if (existing.Length > 0 && existing != "AuthenticatedMessage")
                return false;
            _lines[k] = $"{m.Groups[1].Value}{key}: {value}";
            Dirty = true;
            return true;
        }

        Insert(start + 1, $"{FieldIndent(start, end)}{key}: {value}");
        Dirty = true;
        return true;
    }



    public bool SetWrappedFlag(string path, string key) {
        var (start, end) = RouteBlock(path);
        if (start < 0) return false;

        for (int k = start + 1; k < end; k++) {
            var m = Regex.Match(_lines[k], @"^\s*" + Regex.Escape(key) + @":\s*([^#]*?)\s*(?:#.*)?$");
            if (m.Success) return false;
        }
        Insert(start + 1, $"{FieldIndent(start, end)}{key}: true");
        Dirty = true;
        return true;
    }




    public string CanonicalPath(string path) {
        if (HasPath(path)) return path;
        var slash = path.LastIndexOf('/');
        if (slash <= 0) return path;
        var parent = path[..slash];
        return HasPathParam(parent) ? parent : path;
    }

    private bool HasPathParam(string path) {
        var (start, end) = RouteBlock(path);
        if (start < 0) return false;
        for (int k = start + 1; k < end; k++)
            if (PathParamRegex().IsMatch(_lines[k])) return true;
        return false;
    }



    public bool AddRoute(string path, string? request, bool requestWrapped, string response, bool responseWrapped) {
        if (HasPath(path)) return false;
        if (CanonicalPath(path) != path) return false;

        var ns = path.Split('/')[0];
        int insertAt = SectionInsertPoint(ns);
        if (insertAt < 0) return false;

        var block = new List<string> {
            $"  - path: {path}",
            string.IsNullOrEmpty(request)
            ? "    request:  # TODO review - request type not detected"
            : $"    request: {request}"
        };
        if (requestWrapped) block.Add("    requestWrapped: true");
        block.Add($"    response: {response}");
        if (responseWrapped) block.Add("    responseWrapped: true");

        _lines.InsertRange(insertAt, block);
        Dirty = true;
        return true;
    }



    public bool RemoveFromNeedsCapture(string path) {
        int nc = _lines.FindIndex(l => l.StartsWith("needs_capture:", StringComparison.Ordinal));
        if (nc < 0) return false;
        int end = NextTopLevelKey(nc);

        for (int k = nc + 1; k < end; k++) {

            var m = Regex.Match(_lines[k], @"^\s*-\s+(\S+)\s*(?:#.*)?$");
            if (m.Success && m.Groups[1].Value == path) {
                _lines.RemoveAt(k);
                Dirty = true;
                return true;
            }
        }
        return false;
    }



    private const string NoneMarker = "# none - empty body";


    public bool MarkRequestNone(string path) {
        var (start, end) = RouteBlock(path);
        if (start < 0) return false;
        for (int k = start + 1; k < end; k++) {
            var m = Regex.Match(_lines[k], @"^(\s*)(request|requestType):\s*([^#]*?)\s*(?:#.*)?$");
            if (!m.Success) continue;
            if (m.Groups[3].Value.Trim().Length > 0) return false;
            _lines[k] = $"{m.Groups[1].Value}request:  {NoneMarker}";
            Dirty = true;
            return true;
        }
        Insert(start + 1, $"{FieldIndent(start, end)}request:  {NoneMarker}");
        Dirty = true;
        return true;
    }



    public bool RequestUnresolved(string path) {
        var (start, end) = RouteBlock(path);
        if (start >= 0) {
            for (int k = start + 1; k < end; k++) {
                if (Regex.IsMatch(_lines[k], @"^\s*request:\s*" + Regex.Escape(NoneMarker) + @"\s*$"))
                    return false;
            }
        }

        return FieldUnresolved(path, "request", "requestType");
    }

    public bool ResponseUnresolved(string path) => FieldUnresolved(path, "response", "responseType");

    private bool FieldUnresolved(string path, string key, string legacy) {
        var (start, end) = RouteBlock(path);
        if (start < 0) return true;
        for (int k = start + 1; k < end; k++) {
            var m = Regex.Match(_lines[k], @"^\s*(" + Regex.Escape(key) + "|" + Regex.Escape(legacy) + @"):\s*([^#]*?)\s*(?:#.*)?$");
            if (m.Success) {
                var v = m.Groups[2].Value.Trim();
                return v.Length == 0 || v == "AuthenticatedMessage";
            }
        }
        return true;
    }

    private void Insert(int index, string line) => _lines.Insert(index, line);


    private string FieldIndent(int start, int end) {
        for (int k = start + 1; k < end; k++) {
            var m = Regex.Match(_lines[k], @"^(\s+)\S");
            if (m.Success) return m.Groups[1].Value;
        }
        return Regex.Match(_lines[start], @"^\s*").Value + "  ";
    }

    private static string LegacyAlias(string key) => key switch {
        "request" => "requestType",
        "response" => "responseType",
        _ => key,
    };

    private int FindRouteStart(string path) =>
        _lines.FindIndex(l => Regex.IsMatch(l, @"^\s*-\s+path:\s+" + Regex.Escape(path) + @"\s*$"));


    private (int start, int end) RouteBlock(string path) {
        int start = FindRouteStart(path);
        if (start < 0) return (-1, -1);
        int end = _lines.Count;
        for (int k = start + 1; k < _lines.Count; k++) {
            if (Regex.IsMatch(_lines[k], @"^\s*-\s+path:") || Regex.IsMatch(_lines[k], @"^\w")) { end = k; break; }
        }
        return (start, end);
    }

    private int NextTopLevelKey(int from) {
        for (int k = from + 1; k < _lines.Count; k++)
            if (Regex.IsMatch(_lines[k], @"^\w[\w_]*:")) return k;
        return _lines.Count;
    }



    private int SectionInsertPoint(string ns) {
        int routes = _lines.FindIndex(l => l.StartsWith("routes:", StringComparison.Ordinal));
        if (routes < 0) return -1;
        int routesEnd = NextTopLevelKey(routes);

        int comment = _lines.FindIndex(routes, l => l.Trim() == $"# {ns}/");
        if (comment < 0 || comment >= routesEnd)
            return routesEnd;


        int at = routesEnd;
        for (int k = comment + 1; k < routesEnd; k++) {
            if (_lines[k].TrimStart().StartsWith("# ", StringComparison.Ordinal) && _lines[k].Trim().EndsWith('/')) { at = k; break; }
        }
        while (at > comment && _lines[at - 1].Trim().Length == 0) at--;
        return at;
    }

    [GeneratedRegex(@"^\s*pathParam:\s*true\s*(?:#.*)?$")]
    private static partial Regex PathParamRegex();
}
