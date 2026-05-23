using System.IO.Compression;
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

        var json = FormatResponse(path, inner);
        if (json is null)
        {
            Console.WriteLine("no parser");
            continue;
        }

        var slug = path.Replace('/', '_');
        var outFile = Path.Combine(fixturesOut, slug + ".json");
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
    var outDir = Path.Combine(FindRepoRoot(), "EggIncognito", "Fixtures", "default");
    Directory.CreateDirectory(outDir);

    Console.WriteLine($"Output: {outDir}");
    Console.WriteLine(eid is not null
        ? $"Scrubbing EID: {eid} -> {eidPlaceholder}"
        : "EGG_INC_EID not set - EID scrubbing disabled");
    Console.WriteLine();

    using var doc = JsonDocument.Parse(File.ReadAllBytes(harPath));
    var entries = doc.RootElement.GetProperty("log").GetProperty("entries").EnumerateArray();

    var counts = new HarCounts();
    var seen = new HashSet<string>(StringComparer.Ordinal);
    foreach (var entry in entries)
        ProcessHarEntry(entry, eid, eidPlaceholder, outDir, overwrite, seen, counts);

    Console.WriteLine();
    Console.WriteLine($"new={counts.Wrote}  upd={counts.Upd}  diff={counts.Diff}  same={counts.Same}  err={counts.Err}  -> {outDir}");
}

static void ProcessHarEntry(JsonElement entry, string? eid, string eidPlaceholder,
    string outDir, bool overwrite, HashSet<string> seen, HarCounts counts)
{
    (string path, string json)? decoded;
    try
    {
        decoded = TryDecodeEntry(entry, eid, eidPlaceholder);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"  ERR   {GetEntryPath(entry)}: {ex.Message}");
        counts.Err++;
        return;
    }

    if (decoded is null) return;
    if (!seen.Add(decoded.Value.path)) return;

    var (path, json) = decoded.Value;
    var slug = path.Replace('/', '_');
    switch (WriteFixture(Path.Combine(outDir, slug + ".json"), json, overwrite))
    {
        case "wrote": counts.Wrote++; Console.WriteLine($"  wrote {slug}.json"); break;
        case "upd":   counts.Upd++;   Console.WriteLine($"  upd   {slug}.json"); break;
        case "same":  counts.Same++;  break;
        case "diff":  counts.Diff++;  Console.WriteLine($"  diff  {slug}.json  (use --overwrite to update)"); break;
    }
}

static (string path, string json)? TryDecodeEntry(JsonElement entry, string? eid, string eidPlaceholder)
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

    var outer = Ei.AuthenticatedMessage.Parser.ParseFrom(Convert.FromBase64String(bodyText));
    var inner = outer.Compressed ? Decompress(outer.Message.ToByteArray()) : outer.Message.ToByteArray();

    var json = FormatResponse(path, inner);
    if (json is null) return null;

    return (path, eid is not null ? json.Replace(eid, eidPlaceholder) : json);
}

static string GetEntryPath(JsonElement entry)
{
    try { return NormalizePath(entry.GetProperty("request").GetProperty("url").GetString()!); }
    catch { return "?"; }
}

static string WriteFixture(string path, string json, bool overwrite)
{
    if (!File.Exists(path))
    {
        File.WriteAllText(path, json, Encoding.UTF8);
        return "wrote";
    }
    var existing = File.ReadAllText(path, Encoding.UTF8);
    if (existing == json) return "same";
    if (!overwrite) return "diff";
    File.WriteAllText(path, json, Encoding.UTF8);
    return "upd";
}

static string NormalizePath(string url)
{
    var path = new Uri(url).AbsolutePath.TrimStart('/');
    return Regex.Replace(path, @"/EI\d+$", "");
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

static string? FormatResponse(string path, byte[] inner)
{
    var fmt = JsonFormatter.Default;
    try
    {
        IMessage? msg = path switch
        {
            "ei/auto_join_coop" => Ei.JoinCoopResponse.Parser.ParseFrom(inner),
            "ei/clean_accounts" => Ei.AuthenticatedMessage.Parser.ParseFrom(inner),
            "ei/clear_all_user_data" => Ei.AuthenticatedMessage.Parser.ParseFrom(inner),
            "ei/confirm_royalty_delivery" => Ei.AuthenticatedMessage.Parser.ParseFrom(inner),
            "ei/contract_sim_poll" => Ei.ContractSimPollResponse.Parser.ParseFrom(inner),
            "ei/contract_sim_update" => Ei.AuthenticatedMessage.Parser.ParseFrom(inner),
            "ei/coop_status" => Ei.ContractCoopStatusResponse.Parser.ParseFrom(inner),
            "ei/coop_status_basic" => Ei.ContractCoopStatusResponse.Parser.ParseFrom(inner),
            "ei/create_coop" => Ei.CreateCoopResponse.Parser.ParseFrom(inner),
            "ei/daily_gift_info" => Ei.DailyGiftInfo.Parser.ParseFrom(inner),
            "ei/did_config_change" => Ei.ConfigResponse.Parser.ParseFrom(inner),
            "ei/first_contact_secure" => Ei.EggIncFirstContactResponse.Parser.ParseFrom(inner),
            "ei/get_config" => Ei.ConfigResponse.Parser.ParseFrom(inner),
            "ei/get_contracts" => Ei.ContractsResponse.Parser.ParseFrom(inner),
            "ei/get_events" => Ei.EggIncCurrentEvents.Parser.ParseFrom(inner),
            "ei/get_periodicals" => Ei.PeriodicalsResponse.Parser.ParseFrom(inner),
            "ei/get_sales" => Ei.SalesInfo.Parser.ParseFrom(inner),
            "ei/get_shell_showcase" => Ei.ShellShowcase.Parser.ParseFrom(inner),
            "ei/gift_player_coop_secure" => Ei.AuthenticatedMessage.Parser.ParseFrom(inner),
            "ei/join_coop" => Ei.JoinCoopResponse.Parser.ParseFrom(inner),
            "ei/kick_player_coop" => Ei.AuthenticatedMessage.Parser.ParseFrom(inner),
            "ei/leave_coop" => Ei.AuthenticatedMessage.Parser.ParseFrom(inner),
            "ei/query_coop" => Ei.QueryCoopResponse.Parser.ParseFrom(inner),
            "ei/report_player_coop" => Ei.AuthenticatedMessage.Parser.ParseFrom(inner),
            "ei/save_backup_secure" => Ei.SaveBackupResponse.Parser.ParseFrom(inner),
            "ei/send_chicken_run_coop" => Ei.AuthenticatedMessage.Parser.ParseFrom(inner),
            "ei/showcase_listing_info" => Ei.AuthenticatedMessage.Parser.ParseFrom(inner),
            "ei/showcase_vote" => Ei.AuthenticatedMessage.Parser.ParseFrom(inner),
            "ei/submit_to_showcase" => Ei.AuthenticatedMessage.Parser.ParseFrom(inner),
            "ei/sync_path_of_virtue" => Ei.SyncPathOfVirtueResponse.Parser.ParseFrom(inner),
            "ei/update_coop_permissions" => Ei.UpdateCoopPermissionsResponse.Parser.ParseFrom(inner),
            "ei/update_coop_status_secure" => Ei.ContractCoopStatusUpdateResponse.Parser.ParseFrom(inner),
            "ei/user_data_info" => Ei.UserDataInfoResponse.Parser.ParseFrom(inner),
            "ei_afx/abort_mission" => Ei.AuthenticatedMessage.Parser.ParseFrom(inner),
            "ei_afx/authenticate_artifact" => Ei.AuthenticateArtifactResponse.Parser.ParseFrom(inner),
            "ei_afx/collect_contract_artifacts" => Ei.AuthenticatedMessage.Parser.ParseFrom(inner),
            "ei_afx/collect_season_artifacts" => Ei.AuthenticatedMessage.Parser.ParseFrom(inner),
            "ei_afx/complete_mission" => Ei.CompleteMissionResponse.Parser.ParseFrom(inner),
            "ei_afx/config" => Ei.ArtifactsConfigurationResponse.Parser.ParseFrom(inner),
            "ei_afx/consume_artifact" => Ei.ConsumeArtifactResponse.Parser.ParseFrom(inner),
            "ei_afx/craft_artifact" => Ei.CraftArtifactResponse.Parser.ParseFrom(inner),
            "ei_afx/demote_artifact" => Ei.AuthenticateArtifactResponse.Parser.ParseFrom(inner),
            "ei_afx/get_active_missions_v2" => Ei.GetActiveMissionsResponse.Parser.ParseFrom(inner),
            "ei_afx/launch_mission" => Ei.MissionResponse.Parser.ParseFrom(inner),
            "ei_afx/set_artifact" => Ei.SetArtifactResponse.Parser.ParseFrom(inner),
            "ei_afx/sync_mission" => Ei.GetActiveMissionsResponse.Parser.ParseFrom(inner),
            "ei_ctx/confirm_season_reward" => Ei.AuthenticatedMessage.Parser.ParseFrom(inner),
            "ei_ctx/get_contract_evaluation" => Ei.ContractEvaluation.Parser.ParseFrom(inner),
            "ei_ctx/get_contract_player_info" => Ei.ContractPlayerInfo.Parser.ParseFrom(inner),
            "ei_ctx/get_contracts_archive" => Ei.ContractsArchive.Parser.ParseFrom(inner),
            "ei_ctx/get_leaderboard_info" => Ei.LeaderboardInfo.Parser.ParseFrom(inner),
            "ei_ctx/get_season_infos_v2" => Ei.ContractSeasonInfos.Parser.ParseFrom(inner),
            "ei_data/log_purchase" => Ei.VerifyPurchaseResponse.Parser.ParseFrom(inner),
            "ei_srv/subscription_status" => Ei.AuthenticatedMessage.Parser.ParseFrom(inner),
            _ => null,
        };
        return msg is null ? null : fmt.Format(msg);
    }
    catch
    {
        return null;
    }
}

static byte[] Decompress(byte[] compressed)
{
    using var input = new MemoryStream(compressed);
    using var zlib = new ZLibStream(input, CompressionMode.Decompress);
    using var output = new MemoryStream();
    zlib.CopyTo(output);
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
