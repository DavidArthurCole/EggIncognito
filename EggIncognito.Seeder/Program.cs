using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Google.Protobuf;
using EggIncognito.Seeder;

var harIdx = Array.IndexOf(args, "--from-har");
if (harIdx >= 0 && harIdx + 1 < args.Length)
{
    RunFromHar(args[harIdx + 1], args.Contains("--overwrite"));
    return;
}

var eid = Environment.GetEnvironmentVariable("EGG_INC_EID")
    ?? throw new InvalidOperationException(
        "Set the EGG_INC_EID environment variable before running the seeder.\n" +
        "  PowerShell: $env:EGG_INC_EID = 'EI...'\n" +
        "  Bash:       export EGG_INC_EID='EI...'");

var apiSalt = Environment.GetEnvironmentVariable("EGG_INC_API_SALT")
    ?? throw new InvalidOperationException(
        "Set the EGG_INC_API_SALT environment variable before running the seeder.\n" +
        "  PowerShell: $env:EGG_INC_API_SALT = '...'\n" +
        "  Bash:       export EGG_INC_API_SALT='...'");

var repoRoot = FindRepoRoot();
var liveTypeMap = LoadEndpointTypes(repoRoot);
var fixturesOut = Path.Combine(repoRoot, "EggIncognito", "Fixtures", "eids", eid);
Directory.CreateDirectory(fixturesOut);

Console.WriteLine($"EID   : {eid}");
Console.WriteLine($"Output: {fixturesOut}");
Console.WriteLine();

using var http = new HttpClient(new HttpClientHandler
{
    AutomaticDecompression = System.Net.DecompressionMethods.GZip
});
http.DefaultRequestHeaders.Add("User-Agent", "Dalvik/2.1.0 (Linux; U; Android 9; SM-G960U1 Build/PPR1.180610.011)");
http.DefaultRequestHeaders.Add("Accept-Encoding", "gzip");
http.DefaultRequestHeaders.Add("Connection", "Keep-Alive");

const string auxbrain = "https://www.auxbrain.com/";
const string ctxHost = "https://ctx-dot-auxbrainhome.appspot.com/";

var endpoints = new (string Path, string Base, Func<byte[]?>? Inner, bool Wrap)[]
{
    ("ei/first_contact_secure", auxbrain, () => BuildFirstContact(eid), true),
    ("ei/get_periodicals", auxbrain, () => BuildPeriodicals(eid), false),
    ("ei/get_contracts", auxbrain, () => BuildBasicAuth(eid), false),
    ("ei/get_events", auxbrain, () => BuildBasicAuth(eid), false),
    ("ei/daily_gift_info", auxbrain, () => BuildBasicAuth(eid), false),
    ("ei/user_data_info", auxbrain, () => BuildUserDataInfo(eid), false),
    ("ei_afx/config", auxbrain, () => BuildAfxConfig(eid), false),
    ("ei_afx/sync_mission", auxbrain, () => BuildSyncMission(eid), false),
    ("ei_afx/get_active_missions_v2", auxbrain, () => BuildGetActiveMissions(eid), false),
    ("ei_ctx/get_season_infos_v2", auxbrain, () => BuildBasicAuth(eid), false),
    ("ei_ctx/get_contracts_archive", auxbrain, () => BuildBasicAuth(eid), false),
    ("ei_srv/subscription_status", ctxHost, null, false),
};

foreach (var (path, baseUrl, innerBuilder, wrap) in endpoints)
{
    Console.Write($"  {path,-45} ");
    try
    {
        HttpContent content;
        string requestUrl;

        if (path == "ei_srv/subscription_status")
        {
            requestUrl = $"{baseUrl}{path}/{eid}";
            content = new ByteArrayContent([]);
        }
        else
        {
            var innerBytes = innerBuilder!.Invoke()!;
            var postBytes = wrap ? WrapInAuthMessage(innerBytes, apiSalt) : innerBytes;
            requestUrl = baseUrl + path;
            content = new FormUrlEncodedContent([new KeyValuePair<string, string>("data", Convert.ToBase64String(postBytes))]);
        }

        var response = await http.PostAsync(requestUrl, content);

        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine($"HTTP {(int)response.StatusCode}");
            continue;
        }

        var body = await response.Content.ReadAsStringAsync();
        var respBytes = Convert.FromBase64String(body);

        var authMsg = Ei.AuthenticatedMessage.Parser.ParseFrom(respBytes);
        var inner = authMsg.Compressed
            ? Decompress(authMsg.Message.ToByteArray())
            : authMsg.Message.ToByteArray();

        var json = FormatResponse(path, inner, liveTypeMap);
        if (json is null)
        {
            Console.WriteLine("no parser");
            continue;
        }

        var slug = path;
        var outFile = Path.Combine(fixturesOut, Fixture(slug));
        Directory.CreateDirectory(Path.GetDirectoryName(outFile)!);
        await File.WriteAllTextAsync(outFile, json, Encoding.UTF8);
        Console.WriteLine($"OK -> {slug}.json");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"ERR: {ex.Message}");
    }
}

Console.WriteLine();
Console.WriteLine("Done. Run 'dotnet build' to pick up new fixtures.");

static void RunFromHar(string harPath, bool overwrite)
{
    if (!File.Exists(harPath))
    {
        Console.Error.WriteLine($"HAR file not found: {harPath}");
        Environment.Exit(1);
    }

    var eid = Environment.GetEnvironmentVariable("EGG_INC_EID");
    const string eidPlaceholder = "EI0000000000000000";
    var fixturesRoot = Path.Combine(FindRepoRoot(), "EggIncognito", "Fixtures");
    var outDir = Path.Combine(fixturesRoot, "default");
    var stagedDir = Path.Combine(fixturesRoot, "staged");
    var requestsDir = Path.Combine(fixturesRoot, "requests");
    Directory.CreateDirectory(outDir);

    Console.WriteLine($"Output: {outDir}");
    Console.WriteLine(eid is not null
        ? $"Scrubbing EID: {eid} -> {eidPlaceholder}"
        : "EGG_INC_EID not set - EID scrubbing disabled");
    Console.WriteLine();

    using var doc = JsonDocument.Parse(File.ReadAllBytes(harPath));
    var entries = doc.RootElement.GetProperty("log").GetProperty("entries").EnumerateArray();

    var typeMap = LoadEndpointTypes(FindRepoRoot());
    var requestTypeMap = LoadRequestTypes(FindRepoRoot());
    var counts = new HarCounts();
    var seen = new HashSet<string>(StringComparer.Ordinal);
    var reqErrorSeen = new HashSet<string>(StringComparer.Ordinal);
    var dirs = new HarDirs(outDir, stagedDir, requestsDir, typeMap, requestTypeMap);
    foreach (var entry in entries)
        ProcessHarEntry(entry, eid, eidPlaceholder, dirs, overwrite, seen, counts, reqErrorSeen);

    Console.WriteLine();
    Console.WriteLine($"new={counts.Wrote}  upd={counts.Upd}  diff={counts.Diff}  same={counts.Same}  loss={counts.Loss}  err={counts.Err}  -> {outDir}");
    if (counts.Diff > 0 && !overwrite)
        Console.WriteLine($"Staged diffs -> {stagedDir}  (review, then re-run with --overwrite)");
}

static void ProcessHarEntry(JsonElement entry, string? eid, string eidPlaceholder,
    HarDirs dirs, bool overwrite, HashSet<string> seen, HarCounts counts,
    HashSet<string> reqErrorSeen)
{
    (string path, string json, string? requestJson, string? autoType, int confidence)? decoded;
    try
    {
        decoded = TryDecodeEntry(entry, eid, eidPlaceholder, dirs.TypeMap, dirs.RequestTypeMap, reqErrorSeen);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"  ERR   {GetEntryPath(entry)}: {ex.Message}");
        counts.Err++;
        return;
    }

    if (decoded is null) return;
    if (!seen.Add(decoded.Value.path)) return;

    var (path, json, requestJson, autoType, confidence) = decoded.Value;
    var slug = path;
    if (SeederConfig.AlwaysSkip.Contains(slug)) return;

    var outFile = Path.Combine(dirs.OutDir, Fixture(slug));
    if (File.Exists(outFile))
    {
        var existing = File.ReadAllText(outFile, Encoding.UTF8);
        if (CountJsonObjects(json) < CountJsonObjects(existing))
        {
            counts.Loss++;
            Console.WriteLine($"  loss  {slug}.json  (skipped - fewer objects than existing)");
            return;
        }
    }

    if (autoType is not null)
    {
        var confidenceStr = confidence >= 80 ? $"confidence: {confidence}%" : $"confidence: {confidence}% - verify before committing";
        Console.WriteLine($"  auto  {slug}.json  (detected as {autoType}, {confidenceStr})");
        if (confidence >= 85)
            AddToEndpointsYaml(autoType, path);
    }

    switch (WriteFixture(outFile, json, overwrite))
    {
        case "wrote": counts.Wrote++; Console.WriteLine($"  wrote {slug}.json"); break;
        case "upd":   counts.Upd++;   Console.WriteLine($"  upd   {slug}.json"); break;
        case "same":  counts.Same++;  break;
        case "diff":
            counts.Diff++;
            var stagedFile = Path.Combine(dirs.StagedDir, Fixture(slug));
            Directory.CreateDirectory(Path.GetDirectoryName(stagedFile)!);
            File.WriteAllText(stagedFile, json, Encoding.UTF8);
            Console.WriteLine($"  diff  {slug}.json  (staged)");
            break;
    }

    if (requestJson is not null)
    {
        var reqFile = Path.Combine(dirs.RequestsDir, slug + ".request.json");
        Directory.CreateDirectory(Path.GetDirectoryName(reqFile)!);
        if (!File.Exists(reqFile))
        {
            var scrubbed = eid is not null ? requestJson.Replace(eid, eidPlaceholder) : requestJson;
            File.WriteAllText(reqFile, scrubbed, Encoding.UTF8);
            Console.WriteLine($"  req   {slug}.request.json  (wrote)");
        }
    }
}

static (string path, string json, string? requestJson, string? autoType, int confidence)? TryDecodeEntry(JsonElement entry, string? eid, string eidPlaceholder,
    IReadOnlyDictionary<string, string> typeMap, IReadOnlyDictionary<string, string> requestTypeMap,
    HashSet<string> reqErrorSeen)
{
    var req = entry.GetProperty("request");
    var res = entry.GetProperty("response");

    if (req.GetProperty("method").GetString() != "POST") return null;
    if (res.GetProperty("status").GetInt32() != 200) return null;

    var path = NormalizePath(req.GetProperty("url").GetString()!);

    var contentEl = res.GetProperty("content");
    var rawText = contentEl.GetProperty("text").GetString()!;
    string bodyText;
    if (contentEl.TryGetProperty("encoding", out var enc) && enc.GetString() == "base64")
        bodyText = Encoding.UTF8.GetString(Convert.FromBase64String(rawText)).Trim();
    else
        bodyText = rawText.Trim();

    byte[] respBytes;
    try { respBytes = Convert.FromBase64String(bodyText); }
    catch (FormatException) { return null; }

    var outer = Ei.AuthenticatedMessage.Parser.ParseFrom(respBytes);
    var inner = outer.Compressed ? Decompress(outer.Message.ToByteArray()) : outer.Message.ToByteArray();

    var requestJson = ExtractRequestJson(req, path, requestTypeMap, reqErrorSeen);

    var json = FormatResponse(path, inner, typeMap);
    if (json is null)
    {
        var (detected, detectedJson, confidence) = AutoDetect(inner);
        if (detected is not null && detectedJson is not null)
            return (path, eid is not null ? detectedJson.Replace(eid, eidPlaceholder) : detectedJson, requestJson, detected, confidence);
        return null;
    }

    return (path, eid is not null ? json.Replace(eid, eidPlaceholder) : json, requestJson, null, 0);
}


static string Fixture(string slug) => slug + ".json";

static string GetEntryPath(JsonElement entry)
{
    try { return NormalizePath(entry.GetProperty("request").GetProperty("url").GetString()!); }
    catch { return "?"; }
}

static string WriteFixture(string path, string json, bool overwrite)
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

static string NormalizeFloats(string json) =>
    Regex.Replace(json, @"(?<=[:\[,\s])(-?\d+)\.0(?=[,\}\]\s\r\n])", "$1");

static string ObfuscateCoopIds(string json) =>
    Regex.Replace(json, @"""coopIdentifier"":\s*""([^""]+)""",
        m => $@"""coopIdentifier"": ""{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(m.Groups[1].Value)))[..12].ToLowerInvariant()}""");

static string NormalizePath(string url)
{
    var path = new Uri(url).AbsolutePath.TrimStart('/');
    return Regex.Replace(path, @"/EI\d+.*$", "");
}

static byte[] BuildFirstContact(string eid)
{
    var req = new Ei.EggIncFirstContactRequest
    {
        EiUserId = eid,
        DeviceId = eid,
        ClientVersion = 71,
        Platform = Ei.Platform.Droid,
        Rinfo = BuildRInfo(eid),
    };
    return req.ToByteArray();
}

static byte[] BuildPeriodicals(string eid)
{
    var req = new Ei.GetPeriodicalsRequest
    {
        UserId = eid,
        Rinfo = BuildRInfo(eid),
        Debug = false,
        PiggyFull = true,
        PiggyFoundFull = true,
        SecondsFullGametime = 400000,
        SecondsFullRealtime = 25000000,
        SoulEggs = 1_000_000_000.0,
        CurrentClientVersion = 71,
    };
    return req.ToByteArray();
}

static byte[] BuildBasicAuth(string eid) => BuildRInfo(eid).ToByteArray();

static byte[] BuildUserDataInfo(string eid)
{
    var req = new Ei.UserDataInfoRequest
    {
        UserId = eid,
        DeviceId = eid,
        Rinfo = BuildRInfo(eid),
    };
    return req.ToByteArray();
}

static byte[] BuildAfxConfig(string eid)
{
    var req = new Ei.ArtifactsConfigurationRequest
    {
        ClientVersion = 999,
        Rinfo = BuildRInfo(eid),
    };
    return req.ToByteArray();
}

static byte[] BuildSyncMission(string eid)
{
    var req = new Ei.MissionRequest
    {
        EiUserId = eid,
        Rinfo = BuildRInfo(eid),
    };
    return req.ToByteArray();
}

static byte[] BuildGetActiveMissions(string eid)
{
    var req = new Ei.GetActiveMissionsRequest { Rinfo = BuildRInfo(eid) };
    return req.ToByteArray();
}

static Ei.BasicRequestInfo BuildRInfo(string eid) => new()
{
    EiUserId = eid,
    ClientVersion = 71,
    Version = "1.35.5",
    Build = "111334",
    Platform = "DROID",
    Country = "US",
    Language = "en",
    Debug = false,
};

static byte[] WrapInAuthMessage(byte[] innerBytes, string salt)
{
    var code = ComputeCode(innerBytes, salt);
    var msg = new Ei.AuthenticatedMessage
    {
        Message = ByteString.CopyFrom(innerBytes),
        Code = code,
    };
    return msg.ToByteArray();
}

static string ComputeCode(byte[] messageBytes, string phrase)
{
    var phraseHash = SHA256.HashData(Encoding.UTF8.GetBytes(phrase));
    var salt = Encoding.ASCII.GetBytes(Convert.ToHexString(phraseHash).ToLowerInvariant());

    const uint magic = 0x3b9af419;
    var mutated = (byte[])messageBytes.Clone();
    mutated[magic % (uint)mutated.Length] = 0x1b;

    var combined = new byte[mutated.Length + salt.Length];
    mutated.CopyTo(combined, 0);
    salt.CopyTo(combined, mutated.Length);
    return Convert.ToHexString(SHA256.HashData(combined)).ToLowerInvariant();
}

static string? FormatResponse(string path, byte[] inner, IReadOnlyDictionary<string, string> typeMap)
{
    if (!typeMap.TryGetValue(path, out var typeName)) return null;
    try
    {
        var msg = ParseByTypeName(typeName, inner);
        if (msg is null) return null;
        var formatted = PrettyPrint(JsonFormatter.Default.Format(msg));
        if (path == "ei_ctx/get_contracts_archive") formatted = ObfuscateCoopIds(formatted);
        return formatted;
    }
    catch
    {
        return null;
    }
}

static IMessage? ParseByTypeName(string typeName, byte[] data)
{
    var type = SeederConfig.EiAssembly.GetType($"Ei.{typeName}");
    var parser = type?.GetProperty("Parser", BindingFlags.Public | BindingFlags.Static)
                      ?.GetValue(null) as MessageParser;
    return parser?.ParseFrom(data);
}

static IReadOnlyDictionary<string, string> LoadEndpointTypes(string repoRoot)
{
    var yaml = File.ReadAllText(Path.Combine(repoRoot, "EggIncognito", "EndpointMap", "endpoints.yaml"));
    var result = new Dictionary<string, string>(StringComparer.Ordinal);
    string? currentPath = null;
    foreach (var line in yaml.Split('\n'))
    {
        var pathMatch = Regex.Match(line, @"^\s+-\s+path:\s+(.+)$");
        if (pathMatch.Success) { currentPath = pathMatch.Groups[1].Value.Trim(); continue; }

        if (currentPath is null) continue;

        var typeMatch = Regex.Match(line, @"^\s+responseType:\s+(.+)$");
        if (typeMatch.Success)
        {
            result[currentPath] = typeMatch.Groups[1].Value.Trim();
            currentPath = null;
            continue;
        }

        if (Regex.IsMatch(line, @"^\s+rawResponse:")) currentPath = null;
    }
    return result;
}


static IReadOnlyDictionary<string, string> LoadRequestTypes(string repoRoot)
{
    var yaml = File.ReadAllText(Path.Combine(repoRoot, "EggIncognito", "EndpointMap", "endpoints.yaml"));
    var result = new Dictionary<string, string>(StringComparer.Ordinal);
    string? currentPath = null;
    foreach (var line in yaml.Split('\n'))
    {
        var pathMatch = Regex.Match(line, @"^\s+-\s+path:\s+(.+)$");
        if (pathMatch.Success) { currentPath = pathMatch.Groups[1].Value.Trim(); continue; }

        if (currentPath is null) continue;

        var typeMatch = Regex.Match(line, @"^\s+requestType:\s+(.+)$");
        if (typeMatch.Success)
        {
            result[currentPath] = typeMatch.Groups[1].Value.Trim();
            currentPath = null;
            continue;
        }

        if (Regex.IsMatch(line, @"^\s+rawResponse:")) currentPath = null;
    }
    return result;
}

static string? ExtractRequestJson(JsonElement reqEl, string path, IReadOnlyDictionary<string, string> requestTypeMap,
    HashSet<string> reqErrorSeen)
{
    if (!requestTypeMap.TryGetValue(path, out var typeName)) return null;
    try
    {
        string? dataValue = null;
        if (reqEl.TryGetProperty("postData", out var postData))
        {
            if (postData.TryGetProperty("params", out var parms))
            {
                foreach (var p in parms.EnumerateArray())
                {
                    if (p.TryGetProperty("name", out var name) && name.GetString() == "data")
                    {
                        // form URL encoding: + is decoded as space by mitmproxy - restore it
                        dataValue = p.GetProperty("value").GetString()?.Replace(' ', '+');
                        break;
                    }
                }
            }
            if (dataValue is null && postData.TryGetProperty("text", out var text))
            {
                var raw = text.GetString() ?? "";
                var idx = raw.IndexOf("data=", StringComparison.Ordinal);
                if (idx >= 0) dataValue = Uri.UnescapeDataString(raw[(idx + 5)..].Replace("+", "%2B"));
            }
        }
        if (dataValue is null) return null; // no form body - path-param or empty request

        var reqBytes = Convert.FromBase64String(dataValue);
        var outer = Ei.AuthenticatedMessage.Parser.ParseFrom(reqBytes);
        var inner = outer.Compressed ? Decompress(outer.Message.ToByteArray()) : outer.Message.ToByteArray();
        var msg = ParseByTypeName(typeName, inner);
        if (msg is null)
        {
            if (reqErrorSeen.Add(path))
                Console.Error.WriteLine($"  req   {path}: no parser for requestType '{typeName}'");
            return null;
        }
        return PrettyPrint(JsonFormatter.Default.Format(msg));
    }
    catch (Exception ex)
    {
        if (reqErrorSeen.Add(path))
            Console.Error.WriteLine($"  req   {path}: {ex.GetType().Name}: {ex.Message}");
        return null;
    }
}

static string PrettyPrint(string json)
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

static void AppendInString(StringBuilder sb, char c, ref bool inString, ref bool escape)
{
    sb.Append(c);
    if (c == '\\') escape = true;
    else if (c == '"') inString = false;
}

static void AppendStructural(StringBuilder sb, string json, char c, ref int i, ref int depth, ref bool inString)
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

static void AppendOpen(StringBuilder sb, string json, char open, ref int i, ref int depth)
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

static int CountJsonObjects(string json)
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

static (string? typeName, string? json, int confidence) AutoDetect(byte[] data)
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

    if (bestScore < 2) return (null, null, 0);
    int confidence = secondBestScore == 0 ? 100 : Math.Min(99, (int)((double)bestScore / (bestScore + secondBestScore) * 100));
    return (bestType, bestJson is null ? null : PrettyPrint(bestJson), confidence);
}

static void AddToEndpointsYaml(string typeName, string endpointPath)
{
    var repoRoot = FindRepoRoot();
    var yamlPath = Path.Combine(repoRoot, "EggIncognito", "EndpointMap", "endpoints.yaml");
    var yaml = File.ReadAllText(yamlPath);

    if (yaml.Contains($"path: {endpointPath}")) return;

    var ns = endpointPath.Split('/')[0];
    var sectionComment = $"  # {ns}/";
    var insertAfter = yaml.LastIndexOf(sectionComment);
    if (insertAfter < 0) return;

    var sectionEnd = yaml.IndexOf("\n\n", insertAfter);
    if (sectionEnd < 0) sectionEnd = yaml.Length;

    var newEntry = $"\n  - path: {endpointPath}\n    requestType: AuthenticatedMessage\n    responseType: {typeName}";
    yaml = yaml.Insert(sectionEnd, newEntry);
    File.WriteAllText(yamlPath, yaml);

    yaml = Regex.Replace(yaml, $@"\n  - {Regex.Escape(endpointPath)}[^\n]*", "");
    File.WriteAllText(yamlPath, yaml);

    Console.WriteLine($"  yaml  added {endpointPath} -> {typeName} to endpoints.yaml");
}

static (int score, string? json) TryParseAs(Type type, byte[] data)
{
    try
    {
        var parser = type.GetProperty("Parser", BindingFlags.Public | BindingFlags.Static)
                         ?.GetValue(null) as MessageParser;
        if (parser is null) return (0, null);
        var json = JsonFormatter.Default.Format(parser.ParseFrom(data));
        return (json.Count(c => c == ':'), json);
    }
    catch (InvalidProtocolBufferException)
    {
        return (0, null);
    }
}

static byte[] Decompress(byte[] compressed)
{
    using var input = new MemoryStream(compressed);
    // GZip magic: 1f 8b. ZLib magic: 78 xx. Fall back to GZip for iOS clients.
    Stream decompressor = compressed.Length >= 2 && compressed[0] == 0x1f && compressed[1] == 0x8b
        ? new GZipStream(input, CompressionMode.Decompress)
        : new ZLibStream(input, CompressionMode.Decompress);
    using var output = new MemoryStream();
    decompressor.CopyTo(output);
    decompressor.Dispose();
    return output.ToArray();
}

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir != null)
    {
        if (dir.GetFiles("*.slnx").Length > 0 || dir.GetFiles("*.sln").Length > 0)
            return dir.FullName;
        dir = dir.Parent;
    }
    return Directory.GetCurrentDirectory();
}
