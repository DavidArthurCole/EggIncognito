

using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Google.Protobuf;

namespace EggIncognito.Services;

public sealed partial class EndpointExtractor(HarDirs dirs, string? eid, string eidPlaceholder, bool overwrite) {
    private readonly HarDirs _dirs = dirs;
    private readonly string? _eid = eid;
    private readonly string _eidPlaceholder = eidPlaceholder;
    private readonly bool _overwrite = overwrite;


    private readonly HashSet<string> _seen = [with(StringComparer.Ordinal)];
    private readonly HashSet<string> _reqErrorSeen = [with(StringComparer.Ordinal)];

    public HarCounts Counts { get; } = new();



    public bool Quiet { get; set; }

    public IEndpointWriteObserver? WriteObserver { get; set; }

    private void Out(string line) { if (!Quiet) Console.WriteLine(line); }
    private void Err(string line) { if (!Quiet) Console.Error.WriteLine(line); }

    public static EndpointExtractor ForRepo(string contentRoot, string? eid, string eidPlaceholder, bool overwrite) {
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




    public string? ProcessFlow(string url, string method, int status, string? requestDataB64, string responseBodyB64) {
        if (method != "POST") return null;
        if (status != 200) return null;

        var path = NormalizePath(url);

        DecodedEntry? decoded;
        try {
            decoded = TryDecode(path, requestDataB64, responseBodyB64);
        } catch (Exception ex) {
            Err($"  ERR   {path}: {ex.Message}");
            Counts.Err++;
            return null;
        }

        if (decoded is null) return null;
        if (!_seen.Add(decoded.Path)) return null;

        WriteDecoded(decoded);
        return decoded.Path;
    }




    public string? ForceWriteEndpoint(string url, string method, int status, string? requestDataB64, string responseBodyB64) {
        if (method != "POST" || status != 200) return null;
        var path = NormalizePath(url);

        DecodedEntry? decoded;
        try { decoded = TryDecode(path, requestDataB64, responseBodyB64, isExplicit: true); } catch (Exception ex) { Err($"  ERR   {path}: {ex.Message}"); Counts.Err++; return null; }
        if (decoded is null) return null;
        if (ExtractorConfig.AlwaysSkip.Contains(decoded.Path)) return null;

        WriteDecoded(decoded, forceOverwrite: true, isExplicit: true);
        return decoded.Path;
    }

    public void RunFromHar(string harPath) {
        using var doc = JsonDocument.Parse(File.ReadAllBytes(harPath));
        var entries = doc.RootElement.GetProperty("log").GetProperty("entries").EnumerateArray();
        foreach (var entry in entries)
            ProcessHarEntry(entry);
    }

    public void RunFromMitm(string mitmPath) {
        foreach (var f in MitmFlowReader.Read(File.ReadAllBytes(mitmPath)))
            ProcessFlow(f.Url, f.Method, f.Status, f.RequestDataB64, f.ResponseBodyB64);
    }

    public void ProcessHarEntry(JsonElement entry) {
        var req = entry.GetProperty("request");
        var res = entry.GetProperty("response");

        var method = req.GetProperty("method").GetString() ?? "";
        var status = res.GetProperty("status").GetInt32();
        var url = req.GetProperty("url").GetString()!;


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



    public void Save() => _dirs.Yaml.Save();


    private DecodedEntry? TryDecode(string path, string? requestDataB64, string responseBodyB64, bool isExplicit = false) {
        byte[] respBytes;
        try { respBytes = ProtoFraming.FromBase64Loose(responseBodyB64); } catch (FormatException) { return null; }

        var outer = Ei.AuthenticatedMessage.Parser.ParseFrom(respBytes);
        var inner = outer.Compressed ? ProtoFraming.Decompress(outer.Message.ToByteArray()) : outer.Message.ToByteArray();

        var request = ExtractRequestJson(requestDataB64, path, isExplicit);

        string Scrub(string s) => ScrubEid(s, _eid, _eidPlaceholder);

        var json = FormatResponse(path, inner, _dirs.TypeMap);
        if (json is null) {
            var det = AutoDetect(inner);
            if (det.typeName is null || det.json is null) return null;
            var red = Scrub(Redactor.Redact(det.json));
            return new DecodedEntry(path, red, request, det.typeName, det.bestScore, det.secondBestScore);
        }

        return new DecodedEntry(path, Scrub(json), request, null, 0, 0);
    }



    private void WriteDecoded(DecodedEntry decoded, bool forceOverwrite = false, bool isExplicit = false) {
        var (path, json, request, autoResponseType, respBest, respSecond) = decoded;
        var slug = path;
        if (ExtractorConfig.AlwaysSkip.Contains(slug)) return;

        SelfRepair(path, request, autoResponseType, respBest, respSecond, isExplicit);

        var outFile = Path.Combine(_dirs.OutDir, EndpointFile(slug));
        if (!forceOverwrite && File.Exists(outFile)) {
            var existing = File.ReadAllText(outFile, Encoding.UTF8);
            if (CountJsonFields(json) < CountJsonFields(existing)) {
                Counts.Loss++;
                Out($"  loss  {slug}.json  (skipped - fewer fields than existing)");
                return;
            }
        }

        var writeResult = WriteEndpointFile(outFile, json, _overwrite || forceOverwrite);
        if (writeResult is "wrote" or "upd") WriteObserver?.OnEndpointWritten(path, json);
        switch (writeResult) {
            case "wrote": Counts.Wrote++; Out($"  wrote {slug}.json"); break;
            case "upd": Counts.Upd++; Out($"  upd   {slug}.json"); break;
            case "same": Counts.Same++; break;
            case "diff":
                Counts.Diff++;
                var stagedFile = Path.Combine(_dirs.StagedDir, EndpointFile(slug));
                Directory.CreateDirectory(Path.GetDirectoryName(stagedFile)!);
                File.WriteAllText(stagedFile, json, Encoding.UTF8);
                Out($"  diff  {slug}.json");
                Out($"        code-insiders --diff \"{Path.Combine(_dirs.OutDir, EndpointFile(slug))}\" \"{stagedFile}\"");
                break;
        }

        if (request.Json is not null) {
            var reqFile = Path.Combine(_dirs.RequestsDir, slug + ".request.json");
            Directory.CreateDirectory(Path.GetDirectoryName(reqFile)!);
            if (!File.Exists(reqFile)) {
                var scrubbed = ScrubEid(Redactor.Redact(request.Json), _eid, _eidPlaceholder);
                File.WriteAllText(reqFile, scrubbed, Encoding.UTF8);
                Out($"  req   {slug}.request.json  (wrote)");
            }
        }
    }



    private void SelfRepair(string capturedPath, RequestDecode request, string? autoResponseType,
        int respBest, int respSecond, bool isExplicit = false) {
        var yaml = _dirs.Yaml;


        var path = yaml.CanonicalPath(capturedPath);


        if (request.DetectedType is not null && yaml.RequestUnresolved(path)) {
            if (yaml.SetFieldIfEmpty(path, "request", request.DetectedType)) {
                if (request.DetectedWrapped) yaml.SetWrappedFlag(path, "requestWrapped");
                Counts.Learned.Add($"{path}  request = {request.DetectedType}{(request.DetectedWrapped ? " (wrapped)" : "")}");
                Counts.WroteYaml = true;
            }
        } else if (request.EmptyBody && yaml.RequestUnresolved(path)) {

            if (yaml.MarkRequestNone(path)) {
                Counts.Learned.Add($"{path}  request = none (empty body observed)");
                Counts.WroteYaml = true;
            }
        }
        if (request.FlagNote is not null) Counts.Flagged.Add(request.FlagNote);



        if (autoResponseType is not null) {
            var verdict = ExtractorConfig.ClassifyAutoWrite(respBest, respSecond);

            if (verdict == AutoWriteVerdict.Write || isExplicit) {
                if (!yaml.HasPath(path)) {

                    if (yaml.AddRoute(path, request.DetectedType, request.DetectedWrapped, autoResponseType, responseWrapped: true)) {
                        Counts.Learned.Add($"{path}  added -> response = {autoResponseType} (wrapped)");
                        Counts.WroteYaml = true;
                    }
                } else if (yaml.ResponseUnresolved(path) && yaml.SetFieldIfEmpty(path, "response", autoResponseType)) {
                    yaml.SetWrappedFlag(path, "responseWrapped");
                    Counts.Learned.Add($"{path}  response = {autoResponseType} (wrapped)");
                    Counts.WroteYaml = true;
                }
            } else if (verdict == AutoWriteVerdict.Flag) {
                Counts.Flagged.Add($"{path} response: {autoResponseType} tied on fields - verify with --decode");
            }
        }


        if (!yaml.RequestUnresolved(path) && !yaml.ResponseUnresolved(path))
            if (yaml.RemoveFromNeedsCapture(path)) Counts.WroteYaml = true;
    }




    private RequestDecode ExtractRequestJson(string? dataValue, string path, bool isExplicit = false) {
        try {
            if (string.IsNullOrEmpty(dataValue))


                return new(null, null, false, null) { EmptyBody = true };

            var reqBytes = ProtoFraming.FromBase64Loose(dataValue);


            if (_dirs.RequestTypeMap.TryGetValue(path, out var typeName)) {
                var toParse = _dirs.RequestWrapped.Contains(path) ? ProtoFraming.Unwrap(reqBytes) : reqBytes;
                var msg = ParseByTypeName(typeName, toParse);
                if (msg is null) {
                    if (_reqErrorSeen.Add(path))
                        Err($"  req   {path}: no parser for requestType '{typeName}'");
                    return new(null, null, false, null);
                }
                return new(ProtoJson.PrettyPrint(JsonFormatter.Default.Format(msg)), null, _dirs.RequestWrapped.Contains(path), null);
            }



            var (chosen, useUnwrapped) = BestFraming(reqBytes);
            if (chosen.typeName is null || chosen.json is null) return new(null, null, false, null);

            var verdict = ExtractorConfig.ClassifyAutoWrite(chosen.bestScore, chosen.secondBestScore);
            Out($"  reqauto {path}  request -> {chosen.typeName} ({(useUnwrapped ? "wrapped" : "raw")}, {verdict}, conf {chosen.confidence}%)");



            if (verdict == AutoWriteVerdict.Write || (isExplicit && chosen.typeName is not null))
                return new(chosen.json, chosen.typeName, useUnwrapped, null);


            var note = verdict == AutoWriteVerdict.Flag
                ? $"{path} request: {chosen.typeName} vs runner-up tied on fields - verify with --decode"
                : null;
            return new(chosen.json, null, useUnwrapped, note);
        } catch (InvalidProtocolBufferException ex) when (ex.Message.Contains("ended unexpectedly")) {
            if (_reqErrorSeen.Add(path))
                Err($"  req   {path}: truncated (HAR capture limit)");
            return new(null, null, false, null);
        } catch (Exception ex) {
            if (_reqErrorSeen.Add(path))
                Err($"  req   {path}: {ex.GetType().Name}: {ex.Message}");
            return new(null, null, false, null);
        }
    }


    public void PrintSelfRepairReport() {
        if (Counts.Learned.Count == 0 && Counts.Flagged.Count == 0) return;

        Console.WriteLine();
        Console.WriteLine("Self-repair summary");
        if (Counts.Learned.Count > 0) {
            Console.WriteLine($"  learned ({Counts.Learned.Count}):");
            foreach (var l in Counts.Learned) Console.WriteLine($"    {l}");
        }
        if (Counts.Flagged.Count > 0) {
            Console.WriteLine($"  flagged for review ({Counts.Flagged.Count}):");
            foreach (var f in Counts.Flagged) Console.WriteLine($"    {f}");
        }
        if (Counts.WroteYaml)
            Console.WriteLine("  note: routes.yaml updated -> POST /api/import/endpoint-status/update to refresh endpoint_status");
    }

    public static string? ReadRequestData(JsonElement reqEl) {
        if (!reqEl.TryGetProperty("postData", out var postData)) return null;
        if (postData.TryGetProperty("params", out var parms)) {
            foreach (var p in parms.EnumerateArray()) {
                if (p.TryGetProperty("name", out var name) && name.GetString() == "data")

                    return p.GetProperty("value").GetString()?.Replace(' ', '+');
            }
        }
        if (postData.TryGetProperty("text", out var text)) {


            foreach (var pair in (text.GetString() ?? "").Split('&')) {
                var eq = pair.IndexOf('=');
                if (eq < 0 || pair[..eq] != "data") continue;
                return Uri.UnescapeDataString(pair[(eq + 1)..].Replace("+", "%2B"));
            }
        }
        return null;
    }


    public static string ScrubEid(string text, string? eid, string placeholder) =>
        string.IsNullOrEmpty(eid)
            ? text
            : Regex.Replace(text, Regex.Escape(eid), _ => placeholder, RegexOptions.IgnoreCase);

    private static string EndpointFile(string slug) => slug + ".json";

    private static string WriteEndpointFile(string path, string json, bool overwrite) {
        if (!File.Exists(path)) {
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

    public static string NormalizePath(string url) {
        var path = new Uri(url).AbsolutePath.TrimStart('/');
        return EidSuffixRegex().Replace(path, "");
    }

    public static string? FormatResponse(string path, byte[] inner, IReadOnlyDictionary<string, string> typeMap) {
        if (!typeMap.TryGetValue(path, out var typeName)) return null;
        try {
            var msg = ParseByTypeName(typeName, inner);
            return msg is null ? null : Redactor.Redact(ProtoJson.PrettyPrint(JsonFormatter.Default.Format(msg)));
        } catch {
            return null;
        }
    }

    public static IMessage? ParseByTypeName(string typeName, byte[] data) {
        var type = ExtractorConfig.EiAssembly.GetType($"Ei.{typeName}");
        var parser = type?.GetProperty("Parser", BindingFlags.Public | BindingFlags.Static)
                          ?.GetValue(null) as MessageParser;
        return parser?.ParseFrom(data);
    }

    public static IReadOnlyDictionary<string, string> LoadEndpointTypes(string contentRoot) =>
        LoadInnerTypes(contentRoot, newKey: "response", legacyKey: "responseType");

    public static IReadOnlyDictionary<string, string> LoadRequestTypes(string contentRoot) =>
        LoadInnerTypes(contentRoot, newKey: "request", legacyKey: "requestType");




    public static IReadOnlyDictionary<string, string> LoadRawResponses(string contentRoot) =>
        LoadInnerTypes(contentRoot, newKey: "rawResponse", legacyKey: "rawResponse");



    public static HashSet<string> LoadRequestWrapped(string contentRoot) {
        var yaml = File.ReadAllText(ContentRoot.RoutesYamlPath(contentRoot));
        var result = new HashSet<string>(StringComparer.Ordinal);
        string? currentPath = null;
        bool wrapped = false;

        void Flush() {
            if (currentPath is not null && wrapped) result.Add(currentPath);
            currentPath = null;
            wrapped = false;
        }

        foreach (var line in yaml.Split('\n')) {
            var pathMatch = Regex.Match(line, @"^\s+-\s+path:\s+(.+)$");
            if (pathMatch.Success) { Flush(); currentPath = pathMatch.Groups[1].Value.Trim(); continue; }
            if (currentPath is null) continue;

            if (Regex.IsMatch(line, @"^\s+requestWrapped:\s*true\s*(?:#.*)?$")) wrapped = true;
            else if (Regex.IsMatch(line, @"^\s+requestType:\s*AuthenticatedMessage\s*(?:#.*)?$")) wrapped = true;
        }
        Flush();
        return result;
    }





    private static IReadOnlyDictionary<string, string> LoadInnerTypes(string contentRoot, string newKey, string legacyKey) {
        var yaml = File.ReadAllText(ContentRoot.RoutesYamlPath(contentRoot));
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        string? currentPath = null;
        string? newVal = null, legacyVal = null;

        void Flush() {
            if (currentPath is not null) {
                var v = newVal ?? legacyVal;
                if (v is not null && v.Length > 0 && v != "AuthenticatedMessage")
                    result[currentPath] = v;
            }
            currentPath = null;
            newVal = legacyVal = null;
        }

        foreach (var line in yaml.Split('\n')) {
            var pathMatch = Regex.Match(line, @"^\s+-\s+path:\s+(.+)$");
            if (pathMatch.Success) { Flush(); currentPath = pathMatch.Groups[1].Value.Trim(); continue; }
            if (currentPath is null) continue;


            var m = Regex.Match(line, @"^\s+" + Regex.Escape(newKey) + @":\s*([^#]*?)\s*(?:#.*)?$");
            if (m.Success) { newVal = m.Groups[1].Value.Trim(); continue; }
            m = Regex.Match(line, @"^\s+" + Regex.Escape(legacyKey) + @":\s*([^#]*?)\s*(?:#.*)?$");
            if (m.Success) { legacyVal = m.Groups[1].Value.Trim(); continue; }
        }
        Flush();
        return result;
    }



    internal static int CountJsonFields(string json) {
        int count = 0;
        bool inString = false, escape = false;
        foreach (char c in json) {
            if (escape) { escape = false; continue; }
            if (inString) {
                if (c == '\\') escape = true;
                else if (c == '"') inString = false;
                continue;
            }
            if (c == '"') inString = true;
            else if (c == ':') count++;
        }
        return count;
    }

    public static void RunDecode(string base64) {
        byte[] data;
        try { data = ProtoFraming.FromBase64Loose(base64); } catch (Exception ex) { Console.Error.WriteLine($"not valid base64: {ex.Message}"); return; }

        void Report(string label, byte[] bytes) {
            Console.WriteLine($"=== {label} ({bytes.Length} bytes) ===");
            var ranked = ExtractorConfig.EiAssembly.GetTypes()
                .Where(t => t.Namespace == "Ei" && !t.IsAbstract && typeof(IMessage).IsAssignableFrom(t))
                .Select(t => (name: t.Name, result: TryParseAs(t, bytes)))
                .Where(x => x.result.score > 0)
                .OrderByDescending(x => x.result.score)
                .Take(5)
                .ToList();

            if (ranked.Count == 0) { Console.WriteLine("  (no Ei.* type parses these bytes)"); return; }
            foreach (var (name, (score, _)) in ranked) {
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


        try {
            var outer = Ei.AuthenticatedMessage.Parser.ParseFrom(data);
            if (outer.Message.Length > 0) {
                var inner = outer.Compressed ? ProtoFraming.Decompress(outer.Message.ToByteArray()) : outer.Message.ToByteArray();
                Report($"unwrapped AuthenticatedMessage.message (compressed={outer.Compressed})", inner);
            }
        } catch (InvalidProtocolBufferException) { }
    }




    public static (string? json, string? typeName) DecodeRequestBody(string? knownType, bool wrapped, byte[] bytes) {
        try {
            if (knownType is not null) {
                var candidates = new List<byte[]>();
                var unwrapped = ProtoFraming.TryUnwrap(bytes);
                if (wrapped && unwrapped is not null) { candidates.Add(unwrapped); candidates.Add(bytes); } else { candidates.Add(bytes); if (unwrapped is not null) candidates.Add(unwrapped); }

                IMessage? best = null;
                int bestScore = int.MinValue;
                foreach (var cand in candidates) {
                    IMessage? m;


                    try { m = ParseByTypeName(knownType, cand); } catch (InvalidProtocolBufferException) { continue; }
                    if (m is null) continue;
                    var json = JsonFormatter.Default.Format(m);
                    var exact = m.ToByteArray().AsSpan().SequenceEqual(cand);


                    var score = (exact ? 100_000 : 0) + json.Count(c => c == ':');
                    if (score > bestScore) { bestScore = score; best = m; }
                }
                if (best is not null)
                    return (ProtoJson.PrettyPrint(JsonFormatter.Default.Format(best)), knownType);
            }

            var (chosen, _) = BestFraming(bytes);
            return chosen.json is null ? (null, null) : (chosen.json, chosen.typeName);
        } catch {
            return (null, null);
        }
    }





    private static ((string? typeName, string? json, int confidence, int bestScore, int secondBestScore) result, bool unwrapped) BestFraming(byte[] bytes) {
        var raw = AutoDetect(bytes);
        var unwrappedBytes = ProtoFraming.TryUnwrap(bytes);
        var unw = unwrappedBytes is null ? default : AutoDetect(unwrappedBytes);
        return unwrappedBytes is not null && unw.bestScore > raw.bestScore ? (unw, true) : (raw, false);
    }

    public static (string? typeName, string? json, int confidence, int bestScore, int secondBestScore) AutoDetect(byte[] data) {
        string? bestType = null;
        string? bestJson = null;
        int bestScore = 0;
        int secondBestScore = 0;

        foreach (var type in ExtractorConfig.EiAssembly.GetTypes()
            .Where(t => t.Namespace == "Ei" && !t.IsAbstract && typeof(IMessage).IsAssignableFrom(t))) {
            var (score, json) = TryParseAs(type, data);
            if (score > bestScore) {
                secondBestScore = bestScore;
                bestScore = score;
                bestType = type.Name;
                bestJson = json;
            } else if (score > secondBestScore) {
                secondBestScore = score;
            }
        }

        if (bestScore < 2) return (null, null, 0, bestScore, secondBestScore);



        const int Exact = 1000;
        int confidence;
        if (secondBestScore == 0) {
            confidence = 100;
        } else if (bestScore >= Exact && secondBestScore < Exact) {
            confidence = 99;
        } else if (bestScore >= Exact && secondBestScore >= Exact) {
            int bf = bestScore - Exact, sf = secondBestScore - Exact;
            confidence = bf + sf == 0 ? 50 : Math.Min(99, (int)((double)bf / (bf + sf) * 100));
        } else {
            confidence = Math.Min(99, (int)((double)bestScore / (bestScore + secondBestScore) * 100));
        }

        return (bestType, bestJson is null ? null : ProtoJson.PrettyPrint(bestJson), confidence, bestScore, secondBestScore);
    }

    public static (int score, string? json) TryParseAs(Type type, byte[] data) {
        try {
            if (type.GetProperty("Parser", BindingFlags.Public | BindingFlags.Static)
                             ?.GetValue(null) is not MessageParser parser) {
                return (0, null);
            }

            var msg = parser.ParseFrom(data);
            var json = JsonFormatter.Default.Format(msg);
            var fieldScore = json.Count(c => c == ':');







            bool exact = msg.ToByteArray().AsSpan().SequenceEqual(data);
            return (exact ? 1000 + fieldScore : fieldScore, json);
        } catch (InvalidProtocolBufferException) {
            return (0, null);
        }
    }

    [GeneratedRegex(@"/EI\d+.*$")]
    private static partial Regex EidSuffixRegex();
}
