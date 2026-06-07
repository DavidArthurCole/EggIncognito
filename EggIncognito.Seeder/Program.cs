using System.Security.Cryptography;
using System.Text;
using Google.Protobuf;
using EggIncognito.Services;

// Thin CLI shim over EggIncognito.Services.EndpointExtractor. Three modes:
//   --from-har <file> [--overwrite]   extract endpoints from a HAR capture
//   --decode <base64>                 identify the proto type of a captured blob
//   (no flag)                         live API mode - seed endpoints from the real auxbrain API

var harIdx = Array.IndexOf(args, "--from-har");
if (harIdx >= 0 && harIdx + 1 < args.Length)
{
    RunFromHar(args[harIdx + 1], args.Contains("--overwrite"));
    return;
}

// --decode <base64>: identify the proto type of an arbitrary captured blob. Tries it
// raw and (if it parses as an AuthenticatedMessage) unwrapped, ranking candidate Ei.*
// types by round-trip fidelity. A diagnostic for resolving unknown request/response types.
var decIdx = Array.IndexOf(args, "--decode");
if (decIdx >= 0 && decIdx + 1 < args.Length)
{
    EndpointExtractor.RunDecode(args[decIdx + 1]);
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
var liveTypeMap = EndpointExtractor.LoadEndpointTypes(repoRoot);
var endpointsOut = Path.Combine(repoRoot, "EggIncognito", "Endpoints", "eids", eid);
Directory.CreateDirectory(endpointsOut);

Console.WriteLine($"EID   : {eid}");
Console.WriteLine($"Output: {endpointsOut}");
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
            ? EndpointExtractor.Decompress(authMsg.Message.ToByteArray())
            : authMsg.Message.ToByteArray();

        var json = EndpointExtractor.FormatResponse(path, inner, liveTypeMap);
        if (json is null)
        {
            Console.WriteLine("no parser");
            continue;
        }

        var slug = path;
        var outFile = Path.Combine(endpointsOut, slug + ".json");
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
Console.WriteLine("Done. Run 'dotnet build' to pick up new endpoints.");

static void RunFromHar(string harPath, bool overwrite)
{
    if (!File.Exists(harPath))
    {
        Console.Error.WriteLine($"HAR file not found: {harPath}");
        Environment.Exit(1);
    }

    var eid = Environment.GetEnvironmentVariable("EGG_INC_EID");
    const string eidPlaceholder = "EI0000000000000000";
    var repoRoot = FindRepoRoot();
    var outDir = Path.Combine(repoRoot, "EggIncognito", "Endpoints", "default");
    var stagedDir = Path.Combine(repoRoot, "EggIncognito", "Endpoints", "staged");

    Console.WriteLine($"Output: {outDir}");
    Console.WriteLine(eid is not null
        ? $"Scrubbing EID: {eid} -> {eidPlaceholder}"
        : "EGG_INC_EID not set - EID scrubbing disabled");
    Console.WriteLine();

    var extractor = EndpointExtractor.ForRepo(repoRoot, eid, eidPlaceholder, overwrite);
    extractor.RunFromHar(harPath);
    extractor.Save(); // single write of all learned types

    var counts = extractor.Counts;
    Console.WriteLine();
    Console.WriteLine($"new={counts.Wrote}  upd={counts.Upd}  diff={counts.Diff}  same={counts.Same}  loss={counts.Loss}  err={counts.Err}  -> {outDir}");
    if (counts.Diff > 0 && !overwrite)
    {
        Console.WriteLine($"Staged: {counts.Diff} diff(s) - re-run with --overwrite to apply");
        foreach (var staged in Directory.EnumerateFiles(stagedDir, "*.json", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(stagedDir, staged);
            var orig = Path.Combine(outDir, rel);
            Console.WriteLine($"  code-insiders --diff \"{orig}\" \"{staged}\"");
        }
    }

    extractor.PrintSelfRepairReport();
}

// ---- live-mode request builders + signing (stay in the Seeder; not part of extraction) ----

static byte[] BuildFirstContact(string eid)
{
    var req = new Ei.EggIncFirstContactRequest
    {
        EiUserId = eid,
        DeviceId = eid,
        ClientVersion = 72,
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
        CurrentClientVersion = 72,
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
    ClientVersion = 72,
    Version = "1.35.7",
    Build = "111343",
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
