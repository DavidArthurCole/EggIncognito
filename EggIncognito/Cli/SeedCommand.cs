using System.Text;
using Google.Protobuf;
using EggIncognito.Services;

namespace EggIncognito.Cli;

// Endpoint-acquisition subcommands. Three modes the GUI does not cover:
//   seed              live-API mode - batch-hit the real auxbrain API for a fixed endpoint set and
//                     write decoded JSON to Endpoints/eids/<EID>/ (Inspector send is view-only,
//                     one-at-a-time, no disk write).
//   from-har <file>   replay a HAR into Endpoints/default/ (capture only does live).
//   decode <base64>   auto-detect the proto type of an arbitrary blob (Inspector decodes a known
//                     type only).
// All three are thin shims over EggIncognito.Core (EndpointExtractor + TransportPipeline).
public static class SeedCommand
{
    public static int RunDecode(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: decode <base64>");
            return 1;
        }
        EndpointExtractor.RunDecode(args[0]);
        return 0;
    }

    public static int RunFromHar(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: from-har <capture.har> [--overwrite]");
            return 1;
        }
        var harPath = args[0];
        var overwrite = args.Contains("--overwrite");
        if (!File.Exists(harPath))
        {
            Console.Error.WriteLine($"HAR file not found: {harPath}");
            return 1;
        }

        var eid = Environment.GetEnvironmentVariable("EGG_INC_EID");
        const string eidPlaceholder = "EI0000000000000000";
        var repoRoot = RepoPaths.FindRoot();
        var outDir = Path.Combine(repoRoot, "EggIncognito", "Endpoints", "default");
        var stagedDir = Path.Combine(repoRoot, "EggIncognito", "Endpoints", "staged");

        Console.WriteLine($"Output: {outDir}");
        Console.WriteLine(eid is not null
            ? $"Scrubbing EID: {eid} -> {eidPlaceholder}"
            : "EGG_INC_EID not set - EID scrubbing disabled");
        Console.WriteLine();

        var extractor = EndpointExtractor.ForRepo(repoRoot, eid, eidPlaceholder, overwrite);
        extractor.RunFromHar(harPath);
        extractor.Save();

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
        return 0;
    }

    public static async Task<int> RunSeedAsync(string[] args)
    {
        var eid = Environment.GetEnvironmentVariable("EGG_INC_EID")
            ?? throw new InvalidOperationException(
                "Set the EGG_INC_EID environment variable before seeding.\n" +
                "  PowerShell: $env:EGG_INC_EID = 'EI...'\n" +
                "  Bash:       export EGG_INC_EID='EI...'");

        // Validate up-front so live mode fails fast. TransportPipeline reads this same env var when
        // signing; the value is not needed here beyond the presence check.
        _ = Environment.GetEnvironmentVariable("EGG_INC_API_SALT")
            ?? throw new InvalidOperationException(
                "Set the EGG_INC_API_SALT environment variable before seeding.\n" +
                "  PowerShell: $env:EGG_INC_API_SALT = '...'\n" +
                "  Bash:       export EGG_INC_API_SALT='...'");

        var repoRoot = RepoPaths.FindRoot();
        var liveTypeMap = EndpointExtractor.LoadEndpointTypes(repoRoot);
        var endpointsOut = Path.Combine(repoRoot, "EggIncognito", "Endpoints", "eids", eid);
        Directory.CreateDirectory(endpointsOut);

        // Single signing authority: TransportPipeline owns the AuthenticatedMessage wrap + hash.
        var pipeline = new TransportPipeline();

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
                    // TransportPipeline wraps+signs (when wrap) or passes through, then base64s.
                    var dataB64 = pipeline.Build(innerBytes, wrap).FinalBase64;
                    requestUrl = baseUrl + path;
                    content = new FormUrlEncodedContent([new KeyValuePair<string, string>("data", dataB64)]);
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

                var outFile = Path.Combine(endpointsOut, path + ".json");
                Directory.CreateDirectory(Path.GetDirectoryName(outFile)!);
                await File.WriteAllTextAsync(outFile, json, Encoding.UTF8);
                Console.WriteLine($"OK -> {path}.json");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERR: {ex.Message}");
            }
        }

        Console.WriteLine();
        Console.WriteLine("Done. Run 'dotnet build' to pick up new endpoints.");
        return 0;
    }

    // live-mode request builders + signing inputs (seeding specifics, not part of extraction)

    private static byte[] BuildFirstContact(string eid) => new Ei.EggIncFirstContactRequest
    {
        EiUserId = eid,
        DeviceId = eid,
        ClientVersion = 72,
        Platform = Ei.Platform.Droid,
        Rinfo = BuildRInfo(eid),
    }.ToByteArray();

    private static byte[] BuildPeriodicals(string eid) => new Ei.GetPeriodicalsRequest
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
    }.ToByteArray();

    private static byte[] BuildBasicAuth(string eid) => BuildRInfo(eid).ToByteArray();

    private static byte[] BuildUserDataInfo(string eid) => new Ei.UserDataInfoRequest
    {
        UserId = eid,
        DeviceId = eid,
        Rinfo = BuildRInfo(eid),
    }.ToByteArray();

    private static byte[] BuildAfxConfig(string eid) => new Ei.ArtifactsConfigurationRequest
    {
        ClientVersion = 999,
        Rinfo = BuildRInfo(eid),
    }.ToByteArray();

    private static byte[] BuildSyncMission(string eid) => new Ei.MissionRequest
    {
        EiUserId = eid,
        Rinfo = BuildRInfo(eid),
    }.ToByteArray();

    private static byte[] BuildGetActiveMissions(string eid) => new Ei.GetActiveMissionsRequest
    {
        Rinfo = BuildRInfo(eid),
    }.ToByteArray();

    private static Ei.BasicRequestInfo BuildRInfo(string eid) => new()
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
}
