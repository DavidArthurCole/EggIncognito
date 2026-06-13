// The endpoint-extraction pipeline. Lives in Core so it can be driven two ways with identical behavior:
//   - from a HAR file, via RunFromHar / ProcessHarEntry
//   - in-process per captured flow, via ProcessFlow
// One flow = (url, method, status, requestData, responseBody). Both paths funnel into ProcessFlow so
// the file and live routes can never diverge.

using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Google.Protobuf;

namespace EggIncognito.Services;

public sealed class EndpointExtractor
{
    private readonly HarDirs _dirs;
    private readonly string? _eid;
    private readonly string _eidPlaceholder;
    private readonly bool _overwrite;

    // Per-run dedup + error suppression, shared across every flow.
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private readonly HashSet<string> _reqErrorSeen = new(StringComparer.Ordinal);

    public HarCounts Counts { get; } = new();

    // When true, suppress the per-flow console chatter. The in-app capture path sets this: it derives
    // the per-flow outcome from the Counts delta and shows it on the dashboard, so console output is
    // redundant. The HAR import path leaves it false to keep its server-side log. End-of-run summaries
    // are not gated.
    public bool Quiet { get; set; }

    private void Out(string line) { if (!Quiet) Console.WriteLine(line); }
    private void Err(string line) { if (!Quiet) Console.Error.WriteLine(line); }

    public EndpointExtractor(HarDirs dirs, string? eid, string eidPlaceholder, bool overwrite)
    {
        _dirs = dirs;
        _eid = eid;
        _eidPlaceholder = eidPlaceholder;
        _overwrite = overwrite;
    }

    // Convenience constructor for a repo-rooted run: loads the type maps + yaml editor and ensures the
    // default output dir exists. contentRoot is the directory that directly holds RouteMap/routes.yaml
    // + Endpoints/, hosted or local, with no "EggIncognito" subdir assumption.
    public static EndpointExtractor ForRepo(string contentRoot, string? eid, string eidPlaceholder, bool overwrite)
    {
        var endpointsRoot = Path.Combine(contentRoot, "Endpoints");
        var outDir = Path.Combine(endpointsRoot, "default");
        var stagedDir = Path.Combine(endpointsRoot, "staged");
        var requestsDir = Path.Combine(endpointsRoot, "requests");
        Directory.CreateDirectory(outDir);

        var dirs = new HarDirs(outDir, stagedDir, requestsDir,
            LoadEndpointTypes(contentRoot), LoadRequestTypes(contentRoot), LoadRequestWrapped(contentRoot),
            new RoutesYamlEditor(contentRoot));
        return new EndpointExtractor(dirs, eid, eidPlaceholder, overwrite);
    }

    // Per-flow entry point. The single contract both the HAR and in-process paths use.
    // requestDataB64 is the base64 `data` param value, or null for an empty body. responseBodyB64 is
    // the base64-encoded response body, the AuthenticatedMessage on the wire. Returns the canonical
    // path processed, or null if the flow was skipped.
    public string? ProcessFlow(string url, string method, int status, string? requestDataB64, string responseBodyB64)
    {
        if (method != "POST") return null;
        if (status != 200) return null;

        var path = NormalizePath(url);

        DecodedEntry? decoded;
        try
        {
            decoded = TryDecode(path, requestDataB64, responseBodyB64);
        }
        catch (Exception ex)
        {
            Err($"  ERR   {path}: {ex.Message}");
            Counts.Err++;
            return null;
        }

        if (decoded is null) return null;
        if (!_seen.Add(decoded.Path)) return null;

        WriteDecoded(decoded);
        return decoded.Path;
    }

    // Explicit user-initiated save, the dashboard "Save as endpoint" button. Unlike ProcessFlow this
    // does not dedup, since live capture already added this flow to _seen, and it force-overwrites the
    // existing endpoint. Returns the written path, or null if the flow could not be decoded.
    public string? ForceWriteEndpoint(string url, string method, int status, string? requestDataB64, string responseBodyB64)
    {
        if (method != "POST" || status != 200) return null;
        var path = NormalizePath(url);

        DecodedEntry? decoded;
        try { decoded = TryDecode(path, requestDataB64, responseBodyB64, isExplicit: true); }
        catch (Exception ex) { Err($"  ERR   {path}: {ex.Message}"); Counts.Err++; return null; }
        if (decoded is null) return null;
        if (ExtractorConfig.AlwaysSkip.Contains(decoded.Path)) return null;

        WriteDecoded(decoded, forceOverwrite: true, isExplicit: true);
        return decoded.Path;
    }

    // HAR-file driver. Iterates log.entries[] and feeds each through ProcessFlow.
    public void RunFromHar(string harPath)
    {
        using var doc = JsonDocument.Parse(File.ReadAllBytes(harPath));
        var entries = doc.RootElement.GetProperty("log").GetProperty("entries").EnumerateArray();
        foreach (var entry in entries)
            ProcessHarEntry(entry);
    }

    // mitmproxy .mitm driver. Reads each serialized flow and feeds it through the same ProcessFlow.
    public void RunFromMitm(string mitmPath)
    {
        foreach (var f in MitmFlowReader.Read(File.ReadAllBytes(mitmPath)))
            ProcessFlow(f.Url, f.Method, f.Status, f.RequestDataB64, f.ResponseBodyB64);
    }

    // Pull (url, method, status, requestData, responseBody) out of one HAR entry and process it.
    public void ProcessHarEntry(JsonElement entry)
    {
        var req = entry.GetProperty("request");
        var res = entry.GetProperty("response");

        var method = req.GetProperty("method").GetString() ?? "";
        var status = res.GetProperty("status").GetInt32();
        var url = req.GetProperty("url").GetString()!;

        // Pre-filter cheaply to avoid reading bodies for flows we will skip anyway.
        if (method != "POST" || status != 200) return;

        var contentEl = res.GetProperty("content");
        var rawText = contentEl.GetProperty("text").GetString()!;
        string responseBodyB64;
        if (contentEl.TryGetProperty("encoding", out var enc) && enc.GetString() == "base64")
            responseBodyB64 = Encoding.UTF8.GetString(Convert.FromBase64String(rawText)).Trim();
        else
            responseBodyB64 = rawText.Trim();

        var requestData = ReadRequestData(req);

        ProcessFlow(url, method, status, requestData, responseBodyB64);
    }

    // Flush the yaml editor once all flows are processed. Both the capture tool and the HAR runner
    // call this at the end of a session.
    public void Save() => _dirs.Yaml.Save();

    // Decode a single flow into a DecodedEntry (redacted), or null if it cannot be parsed.
    private DecodedEntry? TryDecode(string path, string? requestDataB64, string responseBodyB64, bool isExplicit = false)
    {
        byte[] respBytes;
        try { respBytes = ProtoFraming.FromBase64Loose(responseBodyB64); }
        catch (FormatException) { return null; }

        var outer = Ei.AuthenticatedMessage.Parser.ParseFrom(respBytes);
        var inner = outer.Compressed ? ProtoFraming.Decompress(outer.Message.ToByteArray()) : outer.Message.ToByteArray();

        var request = ExtractRequestJson(requestDataB64, path, isExplicit);

        string Scrub(string s) => ScrubEid(s, _eid, _eidPlaceholder);

        var json = FormatResponse(path, inner, _dirs.TypeMap); // already redacted
        if (json is null)
        {
            var det = AutoDetect(inner);
            if (det.typeName is null || det.json is null) return null;
            var red = Scrub(Redactor.Redact(det.json));
            return new DecodedEntry(path, red, request, det.typeName, det.bestScore, det.secondBestScore);
        }

        return new DecodedEntry(path, Scrub(json), request, null, 0, 0);
    }

    // Write a decoded entry to disk or stage a diff, tracking the per-outcome counts.
    // forceOverwrite: explicit user save - skip the fewer-objects loss guard and always overwrite.
    private void WriteDecoded(DecodedEntry decoded, bool forceOverwrite = false, bool isExplicit = false)
    {
        var (path, json, request, autoResponseType, respBest, respSecond) = decoded;
        var slug = path;
        if (ExtractorConfig.AlwaysSkip.Contains(slug)) return;

        SelfRepair(path, request, autoResponseType, respBest, respSecond, isExplicit);

        var outFile = Path.Combine(_dirs.OutDir, EndpointFile(slug));
        if (!forceOverwrite && File.Exists(outFile))
        {
            var existing = File.ReadAllText(outFile, Encoding.UTF8);
            if (CountJsonFields(json) < CountJsonFields(existing))
            {
                Counts.Loss++;
                Out($"  loss  {slug}.json  (skipped - fewer fields than existing)");
                return;
            }
        }

        switch (WriteEndpointFile(outFile, json, _overwrite || forceOverwrite))
        {
            case "wrote": Counts.Wrote++; Out($"  wrote {slug}.json"); break;
            case "upd":   Counts.Upd++;   Out($"  upd   {slug}.json"); break;
            case "same":  Counts.Same++;  break;
            case "diff":
                Counts.Diff++;
                var stagedFile = Path.Combine(_dirs.StagedDir, EndpointFile(slug));
                Directory.CreateDirectory(Path.GetDirectoryName(stagedFile)!);
                File.WriteAllText(stagedFile, json, Encoding.UTF8);
                Out($"  diff  {slug}.json");
                Out($"        code-insiders --diff \"{Path.Combine(_dirs.OutDir, EndpointFile(slug))}\" \"{stagedFile}\"");
                break;
        }

        if (request.Json is not null)
        {
            var reqFile = Path.Combine(_dirs.RequestsDir, slug + ".request.json");
            Directory.CreateDirectory(Path.GetDirectoryName(reqFile)!);
            if (!File.Exists(reqFile))
            {
                var scrubbed = ScrubEid(Redactor.Redact(request.Json), _eid, _eidPlaceholder);
                File.WriteAllText(reqFile, scrubbed, Encoding.UTF8);
                Out($"  req   {slug}.request.json  (wrote)");
            }
        }
    }

    // Apply learned types to routes.yaml under the locked rules: fill only empty/placeholder
    // slots, mark wrapping, prune resolved needs_capture entries, collect report lines.
    private void SelfRepair(string capturedPath, RequestDecode request, string? autoResponseType,
        int respBest, int respSecond, bool isExplicit = false)
    {
        var yaml = _dirs.Yaml;
        // A path-param value resolves to its parent endpoint, so we learn types for the real endpoint
        // and never mint a bogus child.
        var path = yaml.CanonicalPath(capturedPath);

        // Request side: a Write-eligible detected type backfills an unresolved request slot.
        if (request.DetectedType is not null && yaml.RequestUnresolved(path))
        {
            if (yaml.SetFieldIfEmpty(path, "request", request.DetectedType))
            {
                if (request.DetectedWrapped) yaml.SetWrappedFlag(path, "requestWrapped");
                Counts.Learned.Add($"{path}  request = {request.DetectedType}{(request.DetectedWrapped ? " (wrapped)" : "")}");
                Counts.WroteYaml = true;
            }
        }
        else if (request.EmptyBody && yaml.RequestUnresolved(path))
        {
            // The endpoint posts no request proto - record that as the resolved answer.
            if (yaml.MarkRequestNone(path))
            {
                Counts.Learned.Add($"{path}  request = none (empty body observed)");
                Counts.WroteYaml = true;
            }
        }
        if (request.FlagNote is not null) Counts.Flagged.Add(request.FlagNote);

        // Response side: a known type came via the type map, nothing to learn. An auto-detected type
        // backfills an unresolved response slot if it passes the same gate.
        if (autoResponseType is not null)
        {
            var verdict = ExtractorConfig.ClassifyAutoWrite(respBest, respSecond);
            // Explicit save confirms an otherwise-ambiguous response type too.
            if (verdict == AutoWriteVerdict.Write || isExplicit)
            {
                if (!yaml.HasPath(path))
                {
                    // Brand-new endpoint. Response from a real capture is always AM-wrapped.
                    if (yaml.AddRoute(path, request.DetectedType, request.DetectedWrapped, autoResponseType, responseWrapped: true))
                    {
                        Counts.Learned.Add($"{path}  added -> response = {autoResponseType} (wrapped)");
                        Counts.WroteYaml = true;
                    }
                }
                else if (yaml.ResponseUnresolved(path) && yaml.SetFieldIfEmpty(path, "response", autoResponseType))
                {
                    yaml.SetWrappedFlag(path, "responseWrapped");
                    Counts.Learned.Add($"{path}  response = {autoResponseType} (wrapped)");
                    Counts.WroteYaml = true;
                }
            }
            else if (verdict == AutoWriteVerdict.Flag)
            {
                Counts.Flagged.Add($"{path} response: {autoResponseType} tied on fields - verify with --decode");
            }
        }

        // Prune needs_capture once the endpoint's slots are resolved.
        if (!yaml.RequestUnresolved(path) && !yaml.ResponseUnresolved(path))
            if (yaml.RemoveFromNeedsCapture(path)) Counts.WroteYaml = true;
    }

    // Decode the request `data` value into redacted JSON for the endpoint's stored request.
    // isExplicit = a user-initiated save vs unattended capture: an ambiguous auto-detect is then
    // treated as confirmed and its type is registered, since the user explicitly chose it.
    private RequestDecode ExtractRequestJson(string? dataValue, string path, bool isExplicit = false)
    {
        try
        {
            if (string.IsNullOrEmpty(dataValue))
                // Body empty or no data param: the endpoint posts no request proto. That is itself a
                // resolved answer - signal it so the caller can record it.
                return new(null, null, false, null) { EmptyBody = true };

            var reqBytes = ProtoFraming.FromBase64Loose(dataValue);

            // Known type: decode under the recorded framing (wrapped or raw).
            if (_dirs.RequestTypeMap.TryGetValue(path, out var typeName))
            {
                var toParse = _dirs.RequestWrapped.Contains(path) ? ProtoFraming.Unwrap(reqBytes) : reqBytes;
                var msg = ParseByTypeName(typeName, toParse);
                if (msg is null)
                {
                    if (_reqErrorSeen.Add(path))
                        Err($"  req   {path}: no parser for requestType '{typeName}'");
                    return new(null, null, false, null);
                }
                return new(ProtoJson.PrettyPrint(JsonFormatter.Default.Format(msg)), null, _dirs.RequestWrapped.Contains(path), null);
            }

            // Unknown type: auto-discover both the inner type and the framing. Try raw and, if it looks
            // like an AuthenticatedMessage, unwrapped; keep whichever framing's best candidate
            // round-trips exactly. The framing is part of the answer, not an input.
            var raw = AutoDetect(reqBytes);
            var unwrappedBytes = ProtoFraming.TryUnwrap(reqBytes);
            var unw = unwrappedBytes is null ? default : AutoDetect(unwrappedBytes);

            bool useUnwrapped = unwrappedBytes is not null && unw.bestScore > raw.bestScore;
            var chosen = useUnwrapped ? unw : raw;
            if (chosen.typeName is null || chosen.json is null) return new(null, null, false, null);

            var verdict = ExtractorConfig.ClassifyAutoWrite(chosen.bestScore, chosen.secondBestScore);
            Out($"  reqauto {path}  request -> {chosen.typeName} ({(useUnwrapped ? "wrapped" : "raw")}, {verdict}, conf {chosen.confidence}%)");

            // Register the type when the auto-verdict is Write, or when the user explicitly saved (their
            // click confirms the otherwise-ambiguous best guess).
            if (verdict == AutoWriteVerdict.Write || (isExplicit && chosen.typeName is not null))
                return new(chosen.json, chosen.typeName, useUnwrapped, null);

            // Flagged or rejected during unattended capture: dump the JSON, do not write.
            var note = verdict == AutoWriteVerdict.Flag
                ? $"{path} request: {chosen.typeName} vs runner-up tied on fields - verify with --decode"
                : null;
            return new(chosen.json, null, useUnwrapped, note);
        }
        catch (InvalidProtocolBufferException ex) when (ex.Message.Contains("ended unexpectedly"))
        {
            if (_reqErrorSeen.Add(path))
                Err($"  req   {path}: truncated (HAR capture limit)");
            return new(null, null, false, null);
        }
        catch (Exception ex)
        {
            if (_reqErrorSeen.Add(path))
                Err($"  req   {path}: {ex.GetType().Name}: {ex.Message}");
            return new(null, null, false, null);
        }
    }

    // End-of-run summary of what the extractor learned and what still needs a human.
    public void PrintSelfRepairReport()
    {
        if (Counts.Learned.Count == 0 && Counts.Flagged.Count == 0) return;

        Console.WriteLine();
        Console.WriteLine("Self-repair summary");
        if (Counts.Learned.Count > 0)
        {
            Console.WriteLine($"  learned ({Counts.Learned.Count}):");
            foreach (var l in Counts.Learned) Console.WriteLine($"    {l}");
        }
        if (Counts.Flagged.Count > 0)
        {
            Console.WriteLine($"  flagged for review ({Counts.Flagged.Count}):");
            foreach (var f in Counts.Flagged) Console.WriteLine($"    {f}");
        }
        if (Counts.WroteYaml)
            Console.WriteLine("  note: routes.yaml updated -> POST /api/import/endpoint-status/update to refresh endpoint_status");
    }

    // Pull the base64 `data` value from a HAR request entry, form param or raw text body.
    public static string? ReadRequestData(JsonElement reqEl)
    {
        if (!reqEl.TryGetProperty("postData", out var postData)) return null;
        if (postData.TryGetProperty("params", out var parms))
        {
            foreach (var p in parms.EnumerateArray())
            {
                if (p.TryGetProperty("name", out var name) && name.GetString() == "data")
                    // mitmproxy decodes form `+` as space - restore it.
                    return p.GetProperty("value").GetString()?.Replace(' ', '+');
            }
        }
        if (postData.TryGetProperty("text", out var text))
        {
            // Form-encoded body: select the `data` key exactly, never a `data=` embedded in another
            // value, and never the trailing `&x=...` params.
            foreach (var pair in (text.GetString() ?? "").Split('&'))
            {
                var eq = pair.IndexOf('=');
                if (eq < 0 || pair[..eq] != "data") continue;
                return Uri.UnescapeDataString(pair[(eq + 1)..].Replace("+", "%2B"));
            }
        }
        return null;
    }

    // Replace every literal rendering of the EID, case-insensitive, including inside larger strings,
    // so re-cased or embedded copies cannot leak. Used for both the endpoint and the redacted request
    // dump. Static so tests can exercise it directly.
    public static string ScrubEid(string text, string? eid, string placeholder) =>
        string.IsNullOrEmpty(eid)
            ? text
            : Regex.Replace(text, Regex.Escape(eid), _ => placeholder, RegexOptions.IgnoreCase);

    private static string EndpointFile(string slug) => slug + ".json";

    private static string WriteEndpointFile(string path, string json, bool overwrite)
    {
        if (!File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, json, Encoding.UTF8);
            return "wrote";
        }
        var existing = File.ReadAllText(path, Encoding.UTF8);
        if (ProtoJson.NormalizeFloats(existing.Trim()) == ProtoJson.NormalizeFloats(json.Trim())) return "same";
        if (!overwrite) return "diff";
        File.WriteAllText(path, json, Encoding.UTF8);
        return "upd";
    }

    public static string NormalizePath(string url)
    {
        var path = new Uri(url).AbsolutePath.TrimStart('/');
        return Regex.Replace(path, @"/EI\d+.*$", "");
    }

    public static string? FormatResponse(string path, byte[] inner, IReadOnlyDictionary<string, string> typeMap)
    {
        if (!typeMap.TryGetValue(path, out var typeName)) return null;
        try
        {
            var msg = ParseByTypeName(typeName, inner);
            if (msg is null) return null;
            return Redactor.Redact(ProtoJson.PrettyPrint(JsonFormatter.Default.Format(msg)));
        }
        catch
        {
            return null;
        }
    }

    public static IMessage? ParseByTypeName(string typeName, byte[] data)
    {
        var type = ExtractorConfig.EiAssembly.GetType($"Ei.{typeName}");
        var parser = type?.GetProperty("Parser", BindingFlags.Public | BindingFlags.Static)
                          ?.GetValue(null) as MessageParser;
        return parser?.ParseFrom(data);
    }

    public static IReadOnlyDictionary<string, string> LoadEndpointTypes(string contentRoot) =>
        LoadInnerTypes(contentRoot, newKey: "response", legacyKey: "responseType");

    public static IReadOnlyDictionary<string, string> LoadRequestTypes(string contentRoot) =>
        LoadInnerTypes(contentRoot, newKey: "request", legacyKey: "requestType");

    // Paths whose response is not a protobuf message: the real API returns a short non-proto ack and
    // the mock serves a literal string. Maps path to the mock's literal. The dashboard uses this to
    // label such responses as acknowledgements instead of "unknown" + hex.
    public static IReadOnlyDictionary<string, string> LoadRawResponses(string contentRoot) =>
        LoadInnerTypes(contentRoot, newKey: "rawResponse", legacyKey: "rawResponse");

    // Paths whose request is wrapped+signed in an AuthenticatedMessage on the wire: explicit
    // `requestWrapped: true`, or the legacy `requestType: AuthenticatedMessage`.
    public static HashSet<string> LoadRequestWrapped(string contentRoot)
    {
        var yaml = File.ReadAllText(ContentRoot.RoutesYamlPath(contentRoot));
        var result = new HashSet<string>(StringComparer.Ordinal);
        string? currentPath = null;
        bool wrapped = false;

        void Flush()
        {
            if (currentPath is not null && wrapped) result.Add(currentPath);
            currentPath = null;
            wrapped = false;
        }

        foreach (var line in yaml.Split('\n'))
        {
            var pathMatch = Regex.Match(line, @"^\s+-\s+path:\s+(.+)$");
            if (pathMatch.Success) { Flush(); currentPath = pathMatch.Groups[1].Value.Trim(); continue; }
            if (currentPath is null) continue;

            if (Regex.IsMatch(line, @"^\s+requestWrapped:\s*true\s*(?:#.*)?$")) wrapped = true;
            else if (Regex.IsMatch(line, @"^\s+requestType:\s*AuthenticatedMessage\s*(?:#.*)?$")) wrapped = true;
        }
        Flush();
        return result;
    }

    // Reads the inner proto type per endpoint, preferring the new `request`/`response` keys and falling
    // back to the legacy `requestType`/`responseType`. The literal "AuthenticatedMessage" in a legacy
    // field means wrapped with inner type unknown, so it is skipped, matching the parser normalization
    // elsewhere.
    private static IReadOnlyDictionary<string, string> LoadInnerTypes(string contentRoot, string newKey, string legacyKey)
    {
        var yaml = File.ReadAllText(ContentRoot.RoutesYamlPath(contentRoot));
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        string? currentPath = null;
        string? newVal = null, legacyVal = null;

        void Flush()
        {
            if (currentPath is not null)
            {
                var v = newVal ?? legacyVal;
                if (v is not null && v.Length > 0 && v != "AuthenticatedMessage")
                    result[currentPath] = v;
            }
            currentPath = null;
            newVal = legacyVal = null;
        }

        foreach (var line in yaml.Split('\n'))
        {
            var pathMatch = Regex.Match(line, @"^\s+-\s+path:\s+(.+)$");
            if (pathMatch.Success) { Flush(); currentPath = pathMatch.Groups[1].Value.Trim(); continue; }
            if (currentPath is null) continue;

            // Value up to an optional inline comment.
            var m = Regex.Match(line, @"^\s+" + Regex.Escape(newKey) + @":\s*([^#]*?)\s*(?:#.*)?$");
            if (m.Success) { newVal = m.Groups[1].Value.Trim(); continue; }
            m = Regex.Match(line, @"^\s+" + Regex.Escape(legacyKey) + @":\s*([^#]*?)\s*(?:#.*)?$");
            if (m.Success) { legacyVal = m.Groups[1].Value.Trim(); continue; }
        }
        Flush();
        return result;
    }

    // Richness signal for the loss guard: populated-field count, the number of ':' outside strings.
    // Object count alone misses a same-shape dump whose fields all collapsed to defaults.
    internal static int CountJsonFields(string json)
    {
        int count = 0;
        bool inString = false, escape = false;
        foreach (char c in json)
        {
            if (escape) { escape = false; continue; }
            if (inString)
            {
                if (c == '\\') escape = true;
                else if (c == '"') inString = false;
                continue;
            }
            if (c == '"') inString = true;
            else if (c == ':') count++;
        }
        return count;
    }

    // --decode diagnostic: identify the proto type of an arbitrary captured blob.
    public static void RunDecode(string base64)
    {
        byte[] data;
        try { data = ProtoFraming.FromBase64Loose(base64); }
        catch (Exception ex) { Console.Error.WriteLine($"not valid base64: {ex.Message}"); return; }

        void Report(string label, byte[] bytes)
        {
            Console.WriteLine($"=== {label} ({bytes.Length} bytes) ===");
            var ranked = ExtractorConfig.EiAssembly.GetTypes()
                .Where(t => t.Namespace == "Ei" && !t.IsAbstract && typeof(IMessage).IsAssignableFrom(t))
                .Select(t => (name: t.Name, result: TryParseAs(t, bytes)))
                .Where(x => x.result.score > 0)
                .OrderByDescending(x => x.result.score)
                .Take(5)
                .ToList();

            if (ranked.Count == 0) { Console.WriteLine("  (no Ei.* type parses these bytes)"); return; }
            foreach (var (name, (score, _)) in ranked)
            {
                var exact = score >= 1000;
                Console.WriteLine($"  {name,-32} score={score}{(exact ? "  [EXACT round-trip]" : "")}");
            }
            var top = ranked[0];
            Console.WriteLine();
            Console.WriteLine($"  best: Ei.{top.name}");
            Console.WriteLine(top.result.json);
            Console.WriteLine();
        }

        Report("raw bytes", data);

        // If it parses as an AuthenticatedMessage with a non-empty payload, rank the inner too.
        try
        {
            var outer = Ei.AuthenticatedMessage.Parser.ParseFrom(data);
            if (outer.Message.Length > 0)
            {
                var inner = outer.Compressed ? ProtoFraming.Decompress(outer.Message.ToByteArray()) : outer.Message.ToByteArray();
                Report($"unwrapped AuthenticatedMessage.message (compressed={outer.Compressed})", inner);
            }
        }
        catch (InvalidProtocolBufferException) { /* not wrapped */ }
    }

    // Shared request-body decode used by both the live capture pipeline and the dashboard decoder, so
    // the proto-framing heuristic can never drift between them. Returns the unredacted JSON + the
    // resolved type name, null if it could not be decoded.
    //   knownType - the routes.yaml-mapped request type, or null to auto-detect.
    //   wrapped   - whether the known type is AuthenticatedMessage-wrapped on the wire.
    // For a known type, parse under the recorded framing; if that yields a poor parse (the lenient
    // proto parser stuffs trailing bytes into the last string field), also try the other framing and
    // keep whichever round-trips exactly or has the richest fields. For an unknown type, auto-detect
    // across raw and AM-unwrapped framings.
    public static (string? json, string? typeName) DecodeRequestBody(string? knownType, bool wrapped, byte[] bytes)
    {
        try
        {
            if (knownType is not null)
            {
                // Candidate framings, preferring the recorded one first.
                var candidates = new List<byte[]>();
                var unwrapped = ProtoFraming.TryUnwrap(bytes);
                if (wrapped && unwrapped is not null) { candidates.Add(unwrapped); candidates.Add(bytes); }
                else { candidates.Add(bytes); if (unwrapped is not null) candidates.Add(unwrapped); }

                IMessage? best = null;
                int bestScore = int.MinValue;
                foreach (var cand in candidates)
                {
                    IMessage? m;
                    // One unusable candidate (e.g. a raw blob that is coincidentally also
                    // AM-unwrappable into garbage) must not discard a good decode from another.
                    try { m = ParseByTypeName(knownType, cand); }
                    catch (InvalidProtocolBufferException) { continue; }
                    if (m is null) continue;
                    var json = JsonFormatter.Default.Format(m);
                    var exact = m.ToByteArray().AsSpan().SequenceEqual(cand);
                    // Exact framing dominates; among non-exact, richer field count wins (the
                    // mojibake parse collapses many fields into one giant string).
                    var score = (exact ? 100_000 : 0) + json.Count(c => c == ':');
                    if (score > bestScore) { bestScore = score; best = m; }
                }
                if (best is not null)
                    return (ProtoJson.PrettyPrint(JsonFormatter.Default.Format(best)), knownType);
            }

            // Unknown type: auto-discover the inner type + framing.
            var raw = AutoDetect(bytes);
            var unw2 = ProtoFraming.TryUnwrap(bytes) is { } u ? AutoDetect(u) : default;
            var chosen = unw2.bestScore > raw.bestScore ? unw2 : raw;
            return chosen.json is null ? (null, null) : (chosen.json, chosen.typeName);
        }
        catch
        {
            return (null, null);
        }
    }

    public static (string? typeName, string? json, int confidence, int bestScore, int secondBestScore) AutoDetect(byte[] data)
    {
        string? bestType = null;
        string? bestJson = null;
        int bestScore = 0;
        int secondBestScore = 0;

        foreach (var type in ExtractorConfig.EiAssembly.GetTypes()
            .Where(t => t.Namespace == "Ei" && !t.IsAbstract && typeof(IMessage).IsAssignableFrom(t)))
        {
            var (score, json) = TryParseAs(type, data);
            if (score > bestScore)
            {
                secondBestScore = bestScore;
                bestScore = score;
                bestType = type.Name;
                bestJson = json;
            }
            else if (score > secondBestScore)
            {
                secondBestScore = score;
            }
        }

        if (bestScore < 2) return (null, null, 0, bestScore, secondBestScore);

        // Confidence. When the winner is an EXACT round-trip (score >= 1000), the 1000 bonus
        // is shared by every exact candidate, so a raw score ratio understates certainty.
        // Compare by field count instead: an exact match with more fields than the runner-up
        // is the more specific (correct) type. Only-one-candidate => 100.
        const int Exact = 1000;
        int confidence;
        if (secondBestScore == 0)
            confidence = 100;
        else if (bestScore >= Exact && secondBestScore < Exact)
            confidence = 99; // sole exact round-trip beats all lenient parses
        else if (bestScore >= Exact && secondBestScore >= Exact)
        {
            // Multiple exact round-trips: rank by field-count margin (strip the shared bonus).
            int bf = bestScore - Exact, sf = secondBestScore - Exact;
            confidence = bf + sf == 0 ? 50 : Math.Min(99, (int)((double)bf / (bf + sf) * 100));
        }
        else
            confidence = Math.Min(99, (int)((double)bestScore / (bestScore + secondBestScore) * 100));

        return (bestType, bestJson is null ? null : ProtoJson.PrettyPrint(bestJson), confidence, bestScore, secondBestScore);
    }

    public static (int score, string? json) TryParseAs(Type type, byte[] data)
    {
        try
        {
            var parser = type.GetProperty("Parser", BindingFlags.Public | BindingFlags.Static)
                             ?.GetValue(null) as MessageParser;
            if (parser is null) return (0, null);
            var msg = parser.ParseFrom(data);
            var json = JsonFormatter.Default.Format(msg);
            var fieldScore = json.Count(c => c == ':');

            // Round-trip fidelity: a type that re-serializes to the EXACT original bytes parsed
            // every byte with no unknown/dropped fields - strong evidence it is the right type.
            // protobuf parsing is lenient (wrong types often "succeed" but lose bytes to unknown
            // fields), so this disambiguates the small-message ties that field-count alone cannot.
            // The big bonus makes an exact round-trip dominate, which is essential for small
            // request messages where field counts are near-identical across candidate types.
            bool exact = msg.ToByteArray().AsSpan().SequenceEqual(data);
            return (exact ? 1000 + fieldScore : fieldScore, json);
        }
        catch (InvalidProtocolBufferException)
        {
            return (0, null);
        }
    }

}
