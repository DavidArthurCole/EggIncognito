// EggIncognito.Core/Services/EndpointExtractor.cs
//
// the endpoint-extraction pipeline. Lives in Core so it can be driven two ways with identical
// behavior:
//   - from a HAR file (the `from-har` CLI subcommand), via RunFromHar / ProcessHarEntry
//   - in-process per captured flow (the capture proxy, via CaptureSession), via ProcessFlow
//
// One flow = (url, method, status, requestData, responseBody). Both paths funnel into
// ProcessFlow so the file and live routes can never diverge.

using System.IO.Compression;
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

    // When true, suppress the per-flow console chatter (capture/diff/loss/req lines). The in-app
    // capture path (CaptureSession) sets this: it derives the per-flow outcome from the Counts delta
    // and shows it on the dashboard, so the console output is redundant there. The `from-har`
    // subcommand leaves it false to keep its operator-facing console log. End-of-run summaries
    // (PrintSelfRepairReport) are not gated.
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

    // Convenience constructor for a repo-rooted run: loads the type maps + yaml editor and
    // ensures the default output dir exists.
    public static EndpointExtractor ForRepo(string repoRoot, string? eid, string eidPlaceholder, bool overwrite)
    {
        var endpointsRoot = Path.Combine(repoRoot, "EggIncognito", "Endpoints");
        var outDir = Path.Combine(endpointsRoot, "default");
        var stagedDir = Path.Combine(endpointsRoot, "staged");
        var requestsDir = Path.Combine(endpointsRoot, "requests");
        Directory.CreateDirectory(outDir);

        var dirs = new HarDirs(outDir, stagedDir, requestsDir,
            LoadEndpointTypes(repoRoot), LoadRequestTypes(repoRoot), LoadRequestWrapped(repoRoot),
            new RoutesYamlEditor(repoRoot));
        return new EndpointExtractor(dirs, eid, eidPlaceholder, overwrite);
    }

    // ---- Per-flow entry point. THE single contract both the HAR and in-process paths use. ----
    //
    // requestDataB64 is the base64 `data` param value (or null for an empty body);
    // responseBodyB64 is the base64-encoded response body (the AuthenticatedMessage on the wire).
    // Returns the canonical path processed, or null if the flow was skipped (non-POST, non-200,
    // undecodable, duplicate, or AlwaysSkip).
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

    // Explicit user-initiated save (the dashboard "Save as endpoint" button). Unlike ProcessFlow this
    // does NOT dedup (the live capture already added this flow to _seen, so ProcessFlow would skip
    // it) and it FORCE-overwrites the existing endpoint (an explicit save means "make this the endpoint"). Returns the written path, or null if the flow could not be decoded.
    public string? ForceWriteEndpoint(string url, string method, int status, string? requestDataB64, string responseBodyB64)
    {
        if (method != "POST" || status != 200) return null;
        var path = NormalizePath(url);

        DecodedEntry? decoded;
        try { decoded = TryDecode(path, requestDataB64, responseBodyB64); }
        catch (Exception ex) { Err($"  ERR   {path}: {ex.Message}"); Counts.Err++; return null; }
        if (decoded is null) return null;
        if (SeederConfig.AlwaysSkip.Contains(decoded.Path)) return null;

        WriteDecoded(decoded, forceOverwrite: true);
        return decoded.Path;
    }

    // ---- HAR-file driver. Iterates log.entries[] and feeds each through ProcessFlow. ----
    public void RunFromHar(string harPath)
    {
        using var doc = JsonDocument.Parse(File.ReadAllBytes(harPath));
        var entries = doc.RootElement.GetProperty("log").GetProperty("entries").EnumerateArray();
        foreach (var entry in entries)
            ProcessHarEntry(entry);
    }

    // Pull (url, method, status, requestData, responseBody) out of one HAR entry and process it.
    public void ProcessHarEntry(JsonElement entry)
    {
        var req = entry.GetProperty("request");
        var res = entry.GetProperty("response");

        var method = req.GetProperty("method").GetString() ?? "";
        var status = res.GetProperty("status").GetInt32();
        var url = req.GetProperty("url").GetString()!;

        // Pre-filter cheaply (avoids reading bodies for flows we will skip anyway).
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

    // Flush the yaml editor once all flows are processed. The capture tool and the HAR runner
    // both call this at the end of a session.
    public void Save() => _dirs.Yaml.Save();

    // ---- decode (was TryDecodeEntry) ----
    private DecodedEntry? TryDecode(string path, string? requestDataB64, string responseBodyB64)
    {
        byte[] respBytes;
        try { respBytes = ProtoFraming.FromBase64Loose(responseBodyB64); }
        catch (FormatException) { return null; }

        var outer = Ei.AuthenticatedMessage.Parser.ParseFrom(respBytes);
        var inner = outer.Compressed ? Decompress(outer.Message.ToByteArray()) : outer.Message.ToByteArray();

        var request = ExtractRequestJson(requestDataB64, path);

        string Scrub(string s) => _eid is not null ? s.Replace(_eid, _eidPlaceholder) : s;

        var json = FormatResponse(path, inner, _dirs.TypeMap); // already redacted by FormatResponse
        if (json is null)
        {
            var det = AutoDetect(inner);
            if (det.typeName is null || det.json is null) return null;
            var red = Scrub(Redactor.Redact(det.json));
            return new DecodedEntry(path, red, request, det.typeName, det.bestScore, det.secondBestScore);
        }

        return new DecodedEntry(path, Scrub(json), request, null, 0, 0);
    }

    // ---- write (was the back half of ProcessHarEntry) ----
    // forceOverwrite: explicit user save - skip the fewer-objects loss guard and always overwrite.
    private void WriteDecoded(DecodedEntry decoded, bool forceOverwrite = false)
    {
        var (path, json, request, autoResponseType, respBest, respSecond) = decoded;
        var slug = path;
        if (SeederConfig.AlwaysSkip.Contains(slug)) return;

        SelfRepair(path, request, autoResponseType, respBest, respSecond);

        var outFile = Path.Combine(_dirs.OutDir, EndpointFile(slug));
        if (!forceOverwrite && File.Exists(outFile))
        {
            var existing = File.ReadAllText(outFile, Encoding.UTF8);
            if (CountJsonObjects(json) < CountJsonObjects(existing))
            {
                Counts.Loss++;
                Out($"  loss  {slug}.json  (skipped - fewer objects than existing)");
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
                var scrubbed = Redactor.Redact(request.Json);
                if (_eid is not null) scrubbed = scrubbed.Replace(_eid, _eidPlaceholder);
                File.WriteAllText(reqFile, scrubbed, Encoding.UTF8);
                Out($"  req   {slug}.request.json  (wrote)");
            }
        }
    }

    // Apply learned types to routes.yaml under the locked rules: fill only empty/placeholder
    // slots, mark wrapping, prune resolved needs_capture entries, collect report lines.
    private void SelfRepair(string capturedPath, RequestDecode request, string? autoResponseType,
        int respBest, int respSecond)
    {
        var yaml = _dirs.Yaml;
        // A path-param value (e.g. get_contract_evaluation/pumpkin-pie) resolves to its parent
        // endpoint, so we learn types for the real endpoint and never mint a bogus child.
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

        // Response side: a known type came via the type map (nothing to learn). An auto-detected
        // type backfills an unresolved response slot if it passes the same gate.
        if (autoResponseType is not null)
        {
            var verdict = SeederConfig.ClassifyAutoWrite(respBest, respSecond);
            if (verdict == AutoWriteVerdict.Write)
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

    // ---- request decode (was ExtractRequestJson, now takes the raw data string) ----
    private RequestDecode ExtractRequestJson(string? dataValue, string path)
    {
        try
        {
            if (string.IsNullOrEmpty(dataValue))
                // Body element present but empty (or no data param): the endpoint posts no request
                // proto. That is itself a resolved answer - signal it so the caller can record it.
                return new(null, null, false, null) { EmptyBody = true };

            var reqBytes = ProtoFraming.FromBase64Loose(dataValue);

            // Known type: decode under the recorded framing (wrapped or raw).
            if (_dirs.RequestTypeMap.TryGetValue(path, out var typeName))
            {
                var toParse = _dirs.RequestWrapped.Contains(path) ? Unwrap(reqBytes) : reqBytes;
                var msg = ParseByTypeName(typeName, toParse);
                if (msg is null)
                {
                    if (_reqErrorSeen.Add(path))
                        Err($"  req   {path}: no parser for requestType '{typeName}'");
                    return new(null, null, false, null);
                }
                return new(PrettyPrint(JsonFormatter.Default.Format(msg)), null, _dirs.RequestWrapped.Contains(path), null);
            }

            // Unknown type: AUTO-DISCOVER both the inner type and the framing. Try raw and (if it
            // looks like an AuthenticatedMessage) unwrapped; keep whichever framing's best
            // candidate round-trips exactly. The framing is part of the answer, not an input.
            var raw = AutoDetect(reqBytes);
            var unwrappedBytes = TryUnwrap(reqBytes);
            var unw = unwrappedBytes is null ? default : AutoDetect(unwrappedBytes);

            bool useUnwrapped = unwrappedBytes is not null && unw.bestScore > raw.bestScore;
            var chosen = useUnwrapped ? unw : raw;
            if (chosen.typeName is null || chosen.json is null) return new(null, null, false, null);

            var verdict = SeederConfig.ClassifyAutoWrite(chosen.bestScore, chosen.secondBestScore);
            Out($"  reqauto {path}  request -> {chosen.typeName} ({(useUnwrapped ? "wrapped" : "raw")}, {verdict}, conf {chosen.confidence}%)");

            if (verdict == AutoWriteVerdict.Write)
                return new(chosen.json, chosen.typeName, useUnwrapped, null);

            // Flagged (ambiguous) or rejected: still dump the JSON, but do not auto-write.
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
            Console.WriteLine("  note: routes.yaml updated -> run scripts/Check-Endpoints.ps1 -Update to refresh endpoint_status");
    }

    // ---- static pure helpers ----

    // Pull the base64 `data` value from a HAR request entry (form param or raw text body).
    public static string? ReadRequestData(JsonElement reqEl)
    {
        if (!reqEl.TryGetProperty("postData", out var postData)) return null;
        if (postData.TryGetProperty("params", out var parms))
        {
            foreach (var p in parms.EnumerateArray())
            {
                if (p.TryGetProperty("name", out var name) && name.GetString() == "data")
                    // form URL encoding: + is decoded as space by mitmproxy - restore it
                    return p.GetProperty("value").GetString()?.Replace(' ', '+');
            }
        }
        if (postData.TryGetProperty("text", out var text))
        {
            var raw = text.GetString() ?? "";
            var idx = raw.IndexOf("data=", StringComparison.Ordinal);
            if (idx >= 0) return Uri.UnescapeDataString(raw[(idx + 5)..].Replace("+", "%2B"));
        }
        return null;
    }

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
        if (NormalizeFloats(existing.Trim()) == NormalizeFloats(json.Trim())) return "same";
        if (!overwrite) return "diff";
        File.WriteAllText(path, json, Encoding.UTF8);
        return "upd";
    }

    private static string NormalizeFloats(string json) =>
        Regex.Replace(json, @"(?<=[:\[,\s])(-?\d+)\.0(?=[,\}\]\s\r\n])", "$1");

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
            return Redactor.Redact(PrettyPrint(JsonFormatter.Default.Format(msg)));
        }
        catch
        {
            return null;
        }
    }

    public static IMessage? ParseByTypeName(string typeName, byte[] data)
    {
        var type = SeederConfig.EiAssembly.GetType($"Ei.{typeName}");
        var parser = type?.GetProperty("Parser", BindingFlags.Public | BindingFlags.Static)
                          ?.GetValue(null) as MessageParser;
        return parser?.ParseFrom(data);
    }

    public static IReadOnlyDictionary<string, string> LoadEndpointTypes(string repoRoot) =>
        LoadInnerTypes(repoRoot, newKey: "response", legacyKey: "responseType");

    public static IReadOnlyDictionary<string, string> LoadRequestTypes(string repoRoot) =>
        LoadInnerTypes(repoRoot, newKey: "request", legacyKey: "requestType");

    // Paths whose request is wrapped+signed in an AuthenticatedMessage on the wire:
    // explicit `requestWrapped: true`, or the legacy `requestType: AuthenticatedMessage`.
    public static HashSet<string> LoadRequestWrapped(string repoRoot)
    {
        var yaml = File.ReadAllText(Path.Combine(repoRoot, "EggIncognito", "RouteMap", "routes.yaml"));
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

    // Reads the inner proto type per endpoint, preferring the new `request`/`response` keys
    // and falling back to the legacy `requestType`/`responseType`. The literal
    // "AuthenticatedMessage" in a legacy field means "wrapped, inner type unknown" - it is
    // skipped (no concrete type to decode with), matching the parser normalization elsewhere.
    private static IReadOnlyDictionary<string, string> LoadInnerTypes(string repoRoot, string newKey, string legacyKey)
    {
        var yaml = File.ReadAllText(Path.Combine(repoRoot, "EggIncognito", "RouteMap", "routes.yaml"));
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

            // Value up to an optional inline `#` comment.
            var m = Regex.Match(line, @"^\s+" + Regex.Escape(newKey) + @":\s*([^#]*?)\s*(?:#.*)?$");
            if (m.Success) { newVal = m.Groups[1].Value.Trim(); continue; }
            m = Regex.Match(line, @"^\s+" + Regex.Escape(legacyKey) + @":\s*([^#]*?)\s*(?:#.*)?$");
            if (m.Success) { legacyVal = m.Groups[1].Value.Trim(); continue; }
        }
        Flush();
        return result;
    }

    // Unwrap an AuthenticatedMessage payload (decompressing if needed). Throws if not wrapped.
    public static byte[] Unwrap(byte[] bytes)
    {
        var outer = Ei.AuthenticatedMessage.Parser.ParseFrom(bytes);
        return outer.Compressed ? Decompress(outer.Message.ToByteArray()) : outer.Message.ToByteArray();
    }

    // Best-effort unwrap: returns null if the bytes are not an AuthenticatedMessage with a payload.
    public static byte[]? TryUnwrap(byte[] bytes)
    {
        try
        {
            var outer = Ei.AuthenticatedMessage.Parser.ParseFrom(bytes);
            if (outer.Message.Length == 0) return null;
            return outer.Compressed ? Decompress(outer.Message.ToByteArray()) : outer.Message.ToByteArray();
        }
        catch (InvalidProtocolBufferException) { return null; }
    }

    public static string PrettyPrint(string json)
    {
        var sb = new StringBuilder(json.Length * 2);
        int depth = 0, i = 0;
        bool inString = false, escape = false;
        while (i < json.Length)
        {
            char c = json[i++];
            if (escape) { sb.Append(c); escape = false; continue; }
            if (inString) { AppendInString(sb, c, ref inString, ref escape); continue; }
            AppendStructural(sb, json, c, ref i, ref depth, ref inString);
        }
        return sb.ToString();
    }

    private static void AppendInString(StringBuilder sb, char c, ref bool inString, ref bool escape)
    {
        sb.Append(c);
        if (c == '\\') escape = true;
        else if (c == '"') inString = false;
    }

    private static void AppendStructural(StringBuilder sb, string json, char c, ref int i, ref int depth, ref bool inString)
    {
        switch (c)
        {
            case ' ': case '\t': case '\r': case '\n': break;
            case '"': inString = true; sb.Append(c); break;
            case '{': case '[': AppendOpen(sb, json, c, ref i, ref depth); break;
            case '}': case ']': sb.AppendLine(); sb.Append(' ', --depth * 2); sb.Append(c); break;
            case ',': sb.Append(c); sb.AppendLine(); sb.Append(' ', depth * 2); break;
            case ':': sb.Append(": "); break;
            default: sb.Append(c); break;
        }
    }

    private static void AppendOpen(StringBuilder sb, string json, char open, ref int i, ref int depth)
    {
        sb.Append(open);
        int j = i;
        while (j < json.Length && char.IsWhiteSpace(json[j])) j++;
        if (j < json.Length && (json[j] == '}' || json[j] == ']'))
        {
            sb.Append(json[j]);
            i = j + 1;
        }
        else
        {
            sb.AppendLine();
            sb.Append(' ', ++depth * 2);
        }
    }

    private static int CountJsonObjects(string json)
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
            else if (c == '{') count++;
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
            var ranked = SeederConfig.EiAssembly.GetTypes()
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

        // If it parses as an AuthenticatedMessage with a non-empty payload, also rank the inner.
        try
        {
            var outer = Ei.AuthenticatedMessage.Parser.ParseFrom(data);
            if (outer.Message.Length > 0)
            {
                var inner = outer.Compressed ? Decompress(outer.Message.ToByteArray()) : outer.Message.ToByteArray();
                Report($"unwrapped AuthenticatedMessage.message (compressed={outer.Compressed})", inner);
            }
        }
        catch (InvalidProtocolBufferException) { /* not wrapped */ }
    }

    // Shared request-body decode used by BOTH the live capture pipeline and the dashboard decoder,
    // so the proto-framing heuristic can never drift between them. Returns the unredacted JSON +
    // the resolved type name (null if it could not be decoded).
    //
    //   knownType - the routes.yaml-mapped request type, or null to auto-detect.
    //   wrapped   - whether the known type is AuthenticatedMessage-wrapped on the wire.
    //
    // For a known type: parse under the recorded framing; if that yields a poor parse (the proto
    // parser is lenient and stuffs trailing bytes into the last string field), also try the OTHER
    // framing and keep whichever round-trips exactly / has the richest fields. For an unknown type:
    // auto-detect across raw and AM-unwrapped framings.
    public static (string? json, string? typeName) DecodeRequestBody(string? knownType, bool wrapped, byte[] bytes)
    {
        try
        {
            if (knownType is not null)
            {
                // Candidate framings, preferring the recorded one first.
                var candidates = new List<byte[]>();
                var unwrapped = TryUnwrap(bytes);
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
                    return (PrettyPrint(JsonFormatter.Default.Format(best)), knownType);
            }

            // Unknown type: auto-discover the inner type + framing.
            var raw = AutoDetect(bytes);
            var unw2 = TryUnwrap(bytes) is { } u ? AutoDetect(u) : default;
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

        foreach (var type in SeederConfig.EiAssembly.GetTypes()
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

        return (bestType, bestJson is null ? null : PrettyPrint(bestJson), confidence, bestScore, secondBestScore);
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

    public static byte[] Decompress(byte[] compressed)
    {
        // GZip: 1f 8b header
        if (compressed.Length >= 2 && compressed[0] == 0x1f && compressed[1] == 0x8b)
        {
            using var i = new MemoryStream(compressed);
            using var gz = new GZipStream(i, CompressionMode.Decompress);
            using var o = new MemoryStream(); gz.CopyTo(o); return o.ToArray();
        }
        // ZLib: try first (has 2-byte header)
        try
        {
            using var i = new MemoryStream(compressed);
            using var zl = new ZLibStream(i, CompressionMode.Decompress);
            using var o = new MemoryStream(); zl.CopyTo(o); return o.ToArray();
        }
        catch (InvalidDataException) { }
        // Raw Deflate: no header
        try
        {
            using var i = new MemoryStream(compressed);
            using var df = new DeflateStream(i, CompressionMode.Decompress);
            using var o = new MemoryStream(); df.CopyTo(o); return o.ToArray();
        }
        catch (InvalidDataException) { }
        // Return raw - Compressed flag may be set but bytes are uncompressed proto
        return compressed;
    }
}
