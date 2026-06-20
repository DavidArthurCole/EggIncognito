using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection; // PersistKeysToDbContext extension (EntityFrameworkCore pkg)
using EggIncognito.Logging;
using EggIncognito.Services;
using EggIncognito.Services.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("EggIncognito.Tests")]

// Build-time hook, not a user command. The EmitDashboardTypes MSBuild target runs
// `dotnet run -- __emit-types <outPath>` to regenerate wwwroot/capture/types.d.ts from the C#
// records, then exits without booting the web host. The old CLI subcommands are now web UI.
if (args.Length >= 2 && args[0] == "__emit-types")
    return EggIncognito.Build.TypeEmitter.Run(args[1]);

// Offline command (not a user feature): carve the .proto from a decrypted iOS Mach-O, Android APK, or a
// bare native .so (auto-detected) and exit. `dotnet run -- __extract-proto <binaryPath> <outPath>`.
// The legacy `__extract-ios-proto` alias is kept so existing scripts keep working.
if (args.Length >= 3 && args[0] is "__extract-proto" or "__extract-ios-proto")
    return EggIncognito.Build.IosProtoExtractor.Run(args[1], args[2]);

// `--capture` starts the proxy once the host is up and opens the Capture tab instead of the
// Inspector. `--eid` / `--label` configure the session via the config keys CaptureSession reads.
var captureMode = args.Contains("--capture");
if (captureMode)
{
    string? ArgValue(string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
    var eid = ArgValue("--eid");
    var label = ArgValue("--label");
    if (eid is not null) Environment.SetEnvironmentVariable("EGG_INC_EID", eid);
    if (label is not null) Environment.SetEnvironmentVariable("CaptureLabel", label);
    if (args.Contains("--overwrite")) Environment.SetEnvironmentVariable("CaptureOverwrite", "true");
}

var builder = WebApplication.CreateBuilder(args);

// EGGINCOGNITO_TEST_DBFREE=1 clears Postgres so integration tests default DB-free.
// Tests that need a DB opt in via WithWebHostBuilder (ConnectionStrings:Postgres wins over this clear).
if (Environment.GetEnvironmentVariable("EGGINCOGNITO_TEST_DBFREE") == "1"
    && string.IsNullOrEmpty(builder.Configuration["TestDbOptIn"]))
{
    builder.Configuration["ConnectionStrings:Postgres"] = "";
}

// Console + one file per process start.
var logsDir = builder.Configuration["LogsPath"]
    ?? Path.Combine(AppContext.BaseDirectory, "logs");
var startupStamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
var fileLogProvider = new FileLoggerProvider(logsDir, startupStamp);
builder.Logging.AddProvider(fileLogProvider);

builder.WebHost.ConfigureKestrel((context, opts) =>
{
    var certsPath = context.Configuration["CertsPath"]
        ?? Path.Combine(AppContext.BaseDirectory, "certs");
    var certFile = Path.Combine(certsPath, "server.crt");
    var keyFile = Path.Combine(certsPath, "server.key");
    if (!File.Exists(certFile) || !File.Exists(keyFile))
    {
        // No custom cert pair: Kestrel keeps its default endpoints. Say so instead of silently
        // skipping, so a misplaced certs dir is diagnosable from the log.
        opts.ApplicationServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("Startup")
            .LogWarning("No TLS cert pair at {CertsPath} (server.crt + server.key) - custom HTTP/HTTPS ports not bound, using default endpoints.", certsPath);
        return;
    }

    var httpPort = int.TryParse(context.Configuration["HttpPort"], out var hp) ? hp : 8080;
    var httpsPort = int.TryParse(context.Configuration["HttpsPort"], out var sp) ? sp : 8443;
    opts.ListenAnyIP(httpPort);
    opts.ListenAnyIP(httpsPort, o => o.UseHttps(X509Certificate2.CreateFromPemFile(certFile, keyFile)));
});

builder.Services.AddControllers();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();

// Behind the reverse proxy (Cloudflare -> origin nginx) TLS is terminated at the edge, so the origin
// sees plain HTTP. Without this, the OAuth challenge builds an http:// redirect_uri that does not match
// the https one registered in the Discord portal ("Invalid OAuth2 redirect_uri"), and generated links
// are http. Honor X-Forwarded-Proto/-Host/-For so the app reconstructs the original https request.
// KnownProxies/KnownNetworks are cleared because the proxy is the sole ingress (the container is only
// reachable over the proxy docker network) - the same trust model as the CF-Connecting-IP rate limiter.
builder.Services.Configure<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto
        | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedHost
        | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor;
    o.KnownProxies.Clear();
    o.KnownIPNetworks.Clear();
});
builder.Services.AddAppRateLimiter(builder.Configuration);
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddHttpClient("inspector", c =>
{
    // Match the real client's headers so auxbrain accepts inspector-built requests.
    c.DefaultRequestHeaders.Add("User-Agent",
        "Dalvik/2.1.0 (Linux; U; Android 9; SM-G960U1 Build/PPR1.180610.011)");
    c.DefaultRequestHeaders.Add("Accept-Encoding", "gzip");
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    AutomaticDecompression = System.Net.DecompressionMethods.GZip,
});
// The "Sealed API proxy" supporter perk: a second inspector egress routed through a configured
// upstream proxy so the downstream API cannot tie the request to this server. Same headers as the
// plain inspector client; only the egress path differs. Unconfigured upstream = direct connection.
var sealedProxyOptions = EggIncognito.Services.SealedProxyOptions.FromConfig(builder.Configuration);
builder.Services.AddSingleton(sealedProxyOptions);
builder.Services.AddSingleton<EggIncognito.Services.ISealedProxy, EggIncognito.Services.SealedProxy>();
builder.Services.AddHttpClient(EggIncognito.Services.SealedProxy.EgressClientName, c =>
{
    c.DefaultRequestHeaders.Add("User-Agent",
        "Dalvik/2.1.0 (Linux; U; Android 9; SM-G960U1 Build/PPR1.180610.011)");
    c.DefaultRequestHeaders.Add("Accept-Encoding", "gzip");
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    AutomaticDecompression = System.Net.DecompressionMethods.GZip,
    Proxy = EggIncognito.Services.SealedProxy.BuildProxy(sealedProxyOptions),
    UseProxy = EggIncognito.Services.SealedProxy.BuildProxy(sealedProxyOptions) is not null,
});
builder.Services.AddSingleton<IAppMode, AppModeService>();
builder.Services.AddSingleton<IBehaviorService, BehaviorService>();
builder.Services.AddSingleton<IProtoReflection, ProtoReflection>();
builder.Services.AddSingleton<IDocRegistry, DocRegistry>();
builder.Services.AddSingleton<ITransportPipeline, TransportPipeline>();

// Endpoints + routes: a file source always; a Postgres overlay + DB-only routes when a connection
// string is configured. With no connection string the app is the file-only Phase 0 app, byte-for-byte.
var pgConn = builder.Configuration.GetConnectionString("Postgres");
var dbEnabled = !string.IsNullOrWhiteSpace(pgConn);

// The file source is registered concretely so the home page can report its endpoint count.
builder.Services.AddSingleton(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var path = config["EndpointsPath"] ?? Path.Combine(AppContext.BaseDirectory, "Endpoints");
    return new FileEndpointSource(path);
});
builder.Services.AddSingleton<IEndpointStore>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<EndpointStore>>();
    var fileSource = sp.GetRequiredService<FileEndpointSource>();
    var scopeFactory = dbEnabled ? sp.GetRequiredService<IServiceScopeFactory>() : null;
    return new EndpointStore(fileSource, scopeFactory, logger);
});

builder.Services.AddSingleton<RouteCatalog>(); // the concrete yaml catalog
// Built from the yaml catalog only: DB routes are dynamic/catch-all and must not pollute the
// static OpenAPI/catalog surface.
builder.Services.AddSingleton<AuxbrainSurface>();
builder.Services.AddSingleton<IRouteCatalog>(sp =>
    new MergedRouteCatalog(
        sp.GetRequiredService<RouteCatalog>(),
        dbEnabled ? sp.GetRequiredService<IDbRouteProvider>() : null));

if (dbEnabled)
{
    builder.Services.AddDbContextPool<EggIncognito.Data.Services.EggIncognitoDbContext>(o => o.UseNpgsql(pgConn));
    // Persist the DataProtection key ring to Postgres so cookie/OAuth tickets survive restarts.
    // Fixed application name required: without it, the purpose derives from the content-root path.
    builder.Services.AddDataProtection()
        .SetApplicationName("EggIncognito")
        .PersistKeysToDbContext<EggIncognito.Data.Services.EggIncognitoDbContext>();
    // Scoped DB endpoint source + a marker so the singleton EndpointStore can resolve it from a scope.
    builder.Services.AddScoped<EggIncognito.Data.Services.DbEndpointSource>();
    builder.Services.AddScoped(sp =>
        new DbEndpointSourceMarker(sp.GetRequiredService<EggIncognito.Data.Services.DbEndpointSource>()));
    // Scoped DB route provider + a singleton adapter that opens a scope per call, since the singleton
    // MergedRouteCatalog cannot capture the scoped provider directly.
    builder.Services.AddScoped<EggIncognito.Data.Services.DbRouteProvider>();
    builder.Services.AddSingleton<IDbRouteProvider>(sp =>
        new EggIncognito.Data.Services.ScopedDbRouteProvider(sp.GetRequiredService<IServiceScopeFactory>()));
}

// Discord auth wires only when a DB + Discord creds are present. AuthState records the result so the
// always-present AuthController + mode endpoint can branch. CurrentUser is always registered and
// reports anonymous when no auth middleware ran, so those consumers construct in both modes.
var authEnabled = builder.AddDiscordAuthIfConfigured(dbEnabled);
builder.Services.AddSingleton(new AuthState(authEnabled));
builder.Services.AddHttpContextAccessor();
builder.Services.TryAddScoped<ICurrentUser, CurrentUser>();
// Supporter role check (login stamp + refresh-benefits). Always registered; without
// Discord:GuildId + Discord:SupporterRoleId + Discord:BotToken it short-circuits to false.
// Short timeout: a slow/rate-limited Discord must not hang login or the refresh-benefits POST for
// the default 100s. Fail-closed past this, the supporter check treats a timeout as "not a supporter".
builder.Services.AddHttpClient("discord-api", c => c.Timeout = TimeSpan.FromSeconds(8));
builder.Services.AddSingleton<SupporterStatus>();
builder.Services.AddSingleton<ISupporterStatus>(sp => sp.GetRequiredService<SupporterStatus>());
// Capture-CA Discord DM: the real REST notifier only when a bot token is configured, else a no-op so
// the web app carries no hard bot dependency. Same gate style as the bot/SupporterStatus wiring.
if (!string.IsNullOrWhiteSpace(builder.Configuration["Discord:BotToken"]))
    builder.Services.AddSingleton<ICaptureCaNotifier, DiscordCaptureCaNotifier>();
else
    builder.Services.AddSingleton<ICaptureCaNotifier, NoopCaptureCaNotifier>();
// The Discord:AdminIds allowlist bootstraps the first admin. Always registered, harmless when empty,
// so UserUpsert can resolve it during login.
builder.Services.AddSingleton(
    EggIncognito.Data.Services.AdminAllowlist.FromConfig(builder.Configuration["Discord:AdminIds"]));

// Discord bot: gateway presence + slash commands. Opt-in, only when Discord:BotToken is set; nothing
// is registered otherwise. Reuses Discord:ClientId as the application id. Optional Discord:GuildId
// also registers commands to one guild for instant testing.
var botToken = builder.Configuration["Discord:BotToken"];
if (!string.IsNullOrWhiteSpace(botToken))
{
    builder.Services.AddSingleton(new EggIncognito.Bot.BotOptions(
        Token: botToken,
        ApplicationId: builder.Configuration["Discord:ClientId"] ?? "",
        GuildId: builder.Configuration["Discord:GuildId"],
        RepoUrl: "https://github.com/DavidArthurCole/EggIncognito",
        // Shared with EggLedger in the same Portainer stack via the flat SHARED_ROLE_ID env var.
        // Falls back to the Discord:SharedRoleId config key for standalone runs.
        SharedRoleId: builder.Configuration["SHARED_ROLE_ID"] ?? builder.Configuration["Discord:SharedRoleId"],
        // /updateserver target: the host-side synckit-agent. Flat env vars in the Portainer stack,
        // Discord:* config keys for standalone runs. Either missing = command answers "not configured".
        DeployAgentUrl: builder.Configuration["DEPLOY_AGENT_URL"] ?? builder.Configuration["Discord:DeployAgentUrl"],
        DeployAgentSecret: builder.Configuration["DEPLOY_AGENT_SECRET"] ?? builder.Configuration["Discord:DeployAgentSecret"],
        // Dev only. Guild-mirrored commands duplicate the global catalog in the Discord UI.
        RegisterGuildCommands: string.Equals(builder.Configuration["Discord:RegisterGuildCommands"], "true", StringComparison.OrdinalIgnoreCase)));
    builder.Services.AddSingleton<EggIncognito.Bot.IStatusProvider, EggIncognito.Services.StatusSnapshotFactory>();
    builder.Services.AddHostedService<EggIncognito.Bot.DiscordBotHostedService>();
}

// Inbound device-farm sync endpoint. Opt-in, only when SyncEvent:EventSecret is set, matching the
// bot (Discord:BotToken) and DB (ConnectionStrings:Postgres) patterns. When absent, EventsController
// 404s and nothing below is registered. The ingest service is also gated at runtime by IAppMode, so
// hosted rejects it even if a secret leaks into a public deploy.
var eventSecret = builder.Configuration["SyncEvent:EventSecret"];
if (!string.IsNullOrWhiteSpace(eventSecret))
{
    var syncContentRoot = ContentRoot.Resolve(builder.Configuration["ContentRoot"]);
    var syncOptions = new SyncEventOptions
    {
        EventSecret = eventSecret,
        ApkFetchRoot = builder.Configuration["SyncEvent:ApkFetchRoot"] ?? "",
    };
    builder.Services.AddSingleton(syncOptions);
    builder.Services.AddSingleton<EggIncognito.Bot.ISyncNotifier, DiscordSyncNotifier>();
    builder.Services.AddSingleton(sp =>
    {
        // Expected proto identity, computed once from the frozen ei.proto. Compared against each
        // event's protoSha to split regen-into-staged from flag-for-manual-refresh.
        var expectedProtoSha = EggIncognito.Core.ProtoHash.Current(syncContentRoot);
        var notifier = sp.GetRequiredService<EggIncognito.Bot.ISyncNotifier>();
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("sync.ingest");

        // Registry: upsert a proto_versions row for every build the farm reports, storing the .proto
        // text + parsed message index when present. Runs independently of the regen/refresh split, so
        // the registry captures even builds whose proto changed and still await a manual ei.proto refresh.
        // No DB configured => no-op. A later phase appends a feed dispatch off the returned tuple here.
        async Task Registry(EggIncognito.Core.Models.NewVersionEvent evt, CancellationToken ct)
        {
            using var scope = sp.CreateScope();
            var store = scope.ServiceProvider.GetService<EggIncognito.Data.Services.ProtoRegistryStore>();
            if (store is null) return; // no DB configured
            string? protoText = string.IsNullOrEmpty(evt.ProtoTextB64) ? null
                : System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(evt.ProtoTextB64));
            // appVersion falls back to the legacy single version when old emitters omit it; build
            // falls back to version too so a legacy event still keys some row (build is the row key).
            var appVersion = string.IsNullOrEmpty(evt.AppVersion) ? evt.Version : evt.AppVersion;
            var build = string.IsNullOrEmpty(evt.Build) ? evt.Version : evt.Build;
            // A keyless event (no build/appVersion at all) must not write a stub registry row.
            if (string.IsNullOrEmpty(build) || string.IsNullOrEmpty(appVersion)) return;
            var (row, created, protoChanged) = await store.UpsertAsync(
                evt.Platform, appVersion, build, evt.ClientVersion, evt.Package, evt.ProtoSha, evt.ApkRef,
                DateTimeOffset.TryParse(evt.DetectedAt, out var dt) ? dt : DateTimeOffset.UtcNow,
                detectedBy: null, protoText, source: "farm", ct: ct);

            // Fan the event out to matching active subscriptions. Created/ProtoChanged are the trigger
            // signal; the dispatcher is best-effort and DB-gated, so no DB or no subs is a no-op.
            var dispatcher = scope.ServiceProvider.GetService<EggIncognito.Services.Feed.FeedDispatcher>();
            if (dispatcher is not null)
            {
                var cfg = scope.ServiceProvider.GetService<IConfiguration>();
                var pageUrl = EggIncognito.Services.Feed.FeedDispatcher.BuildPageUrl(
                    cfg?["Feed:PageBaseUrl"], evt.Platform, build);
                await dispatcher.DispatchAsync(row.Id, evt.Platform, appVersion, build, evt.ClientVersion,
                    evt.ProtoSha, created, protoChanged, pageUrl, ct);
            }
        }

        // Fetch apkRef under ApkFetchRoot. URL fetch is a future add; missing artifact is tolerated.
        Task Fetch(EggIncognito.Core.Models.NewVersionEvent evt, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(syncOptions.ApkFetchRoot) || string.IsNullOrEmpty(evt.ApkRef))
            {
                logger.LogInformation("sync: no ApkFetchRoot or apkRef for {Version}, skipping fetch", evt.Version);
                return Task.CompletedTask;
            }
            var apk = Path.Combine(syncOptions.ApkFetchRoot, evt.ApkRef.TrimStart('/', '\\'));
            if (!File.Exists(apk))
                logger.LogWarning("sync: apk not found at {Apk} for {Version}", apk, evt.Version);
            return Task.CompletedTask;
        }

        // Regen: ensure the staged/ output area exists via the same EndpointExtractor.ForRepo path the
        // HAR + capture routes use, never touching default/. APK-driven endpoint extraction has no
        // decoder yet, so this stages the area and logs; promotion stays a human step.
        Task Regen(EggIncognito.Core.Models.NewVersionEvent evt, CancellationToken ct)
        {
            EndpointExtractor.ForRepo(syncContentRoot, eid: null, "EI0000000000000000", overwrite: true);
            logger.LogInformation("sync: staged area ready for {Version}; apk-driven regen not yet wired", evt.Version);
            return Task.CompletedTask;
        }

        // Stash: a changed proto is flagged, never auto-applied. Write a small manifest under
        // Endpoints/staged/proto-refresh/ recording the version and the sha delta for the human gate.
        Task Stash(EggIncognito.Core.Models.NewVersionEvent evt, CancellationToken ct)
        {
            var stashDir = Path.Combine(syncContentRoot, "Endpoints", "staged", "proto-refresh");
            Directory.CreateDirectory(stashDir);
            var manifest = System.Text.Json.JsonSerializer.Serialize(new
            {
                version = evt.Version,
                oldProtoSha = expectedProtoSha,
                newProtoSha = evt.ProtoSha,
                apkRef = evt.ApkRef,
                detectedAt = evt.DetectedAt,
            });
            File.WriteAllText(Path.Combine(stashDir, $"{evt.Version}.json"), manifest);
            logger.LogWarning("sync: proto changed for {Version}, stashed refresh manifest", evt.Version);
            return Task.CompletedTask;
        }

        return new NewVersionIngestService(expectedProtoSha, notifier, Registry, Fetch, Regen, Stash);
    });
}

// Per-user capture sessions. Local resolves the single anonymous LocalKey session through the
// manager, so DI consumers of CaptureSession (capture-mode autostart, the local controller path)
// behave exactly as the old singleton did.
var hostedCaptureOpts = EggIncognito.Capture.HostedCaptureOptions.Bind(builder.Configuration);
builder.Services.AddSingleton(hostedCaptureOpts);
builder.Services.AddSingleton(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    // Content root = the directory that directly holds RouteMap/ + Endpoints/: the project dir in dev,
    // the exe dir when published. CaptureSession + EndpointExtractor both resolve their files under it.
    var contentRoot = ContentRoot.Resolve(config["ContentRoot"]);
    return new EggIncognito.Capture.CaptureSessionManager(hostedCaptureOpts, (key, basePort) =>
    {
        if (key == EggIncognito.Capture.CaptureSessionManager.LocalKey)
        {
            var capturePath = config["CapturePath"] ?? Path.Combine(contentRoot, "captures");
            var caPath = config["CaPath"] ?? Path.Combine(capturePath, "eggincognito-ca.cer");
            var opts = new EggIncognito.Capture.CaptureSessionOptions(
                Port: int.TryParse(config["CapturePort"], out var cp) ? cp : 8080,
                Eid: config["EGG_INC_EID"] ?? Environment.GetEnvironmentVariable("EGG_INC_EID"),
                Label: config["CaptureLabel"],
                Overwrite: config.GetValue("CaptureOverwrite", false),
                Verbose: config.GetValue("CaptureVerbose", false),
                CapturePath: capturePath,
                CaPath: caPath);
            return new EggIncognito.Capture.CaptureSession(contentRoot, opts);
        }
        // Hosted per-user session: pooled loopback base port, private temp dirs, no endpoint-file
        // writes, no LAN forwarder and no OS trust-store install; the front door is the only way in.
        var dir = Path.Combine(Path.GetTempPath(), "eggincognito-hosted-capture", key);
        var hostedOpts = new EggIncognito.Capture.CaptureSessionOptions(
            Port: basePort, Eid: null, Label: null, Overwrite: false,
            Verbose: config.GetValue("CaptureVerbose", false),
            CapturePath: dir, CaPath: Path.Combine(dir, "ca.cer"),
            WriteEndpoints: false);
        return new EggIncognito.Capture.CaptureSession(contentRoot, hostedOpts,
            verbose => new EggIncognito.Capture.UnobtaniumCaptureProxy(verbose)
            {
                LanForwarderEnabled = false,
                TrustCaInOsStore = false,
            });
    });
});
builder.Services.AddSingleton(sp =>
    sp.GetRequiredService<EggIncognito.Capture.CaptureSessionManager>()
        .GetOrCreate(EggIncognito.Capture.CaptureSessionManager.LocalKey));

// Hosted capture: front door + sweeper, only on a Hosted deploy that opted in. The front door's
// token lookup opens a DI scope per call to reach the scoped credential store.
var hostedCaptureOn = string.Equals(builder.Configuration["AppMode"], "Hosted", StringComparison.OrdinalIgnoreCase)
    && builder.Configuration.GetValue("HostedCaptureEnabled", false);
if (dbEnabled)
{
    builder.Services.AddScoped<EggIncognito.Data.Services.CaptureCredentialStore>();
    builder.Services.AddScoped<EggIncognito.Data.Services.CaptureAddressStore>();
    builder.Services.AddScoped<EggIncognito.Data.Services.ProtoRegistryStore>();
    builder.Services.AddScoped<EggIncognito.Data.Services.StagedProtoStore>();
    builder.Services.AddScoped<EggIncognito.Data.Services.DeviceStatusStore>();
    builder.Services.AddScoped<EggIncognito.Data.Services.IDeviceStatusStore>(
        sp => sp.GetRequiredService<EggIncognito.Data.Services.DeviceStatusStore>());
    builder.Services.AddScoped<EggIncognito.Data.Services.FeedSubscriptionStore>();
    builder.Services.AddScoped<EggIncognito.Data.Services.IFeedSubscriptionStore>(
        sp => sp.GetRequiredService<EggIncognito.Data.Services.FeedSubscriptionStore>());
    builder.Services.AddScoped<EggIncognito.Services.Feed.FeedDispatcher>();
    // Proto backfill importers (admin-triggered, on-demand). The store seam is scoped, so each importer
    // opens its own DI scope inside RunAsync. The "github" named client carries the optional GITHUB_TOKEN
    // and is reused by the plain store fetches too. Only when a DB is configured (no store otherwise).
    builder.Services.AddScoped<EggIncognito.Data.Services.IProtoBackfillStore>(
        sp => sp.GetRequiredService<EggIncognito.Data.Services.ProtoRegistryStore>());
    builder.Services.AddHttpClient("github");
    builder.Services.AddScoped<EggIncognito.Services.Backfill.IGitHubClient, EggIncognito.Services.Backfill.GitHubClient>();
    builder.Services.AddScoped<EggIncognito.Services.Backfill.ElgranjeroImporter>();
    builder.Services.AddScoped<EggIncognito.Services.Backfill.PlayStoreImporter>();
    builder.Services.AddScoped<EggIncognito.Services.Backfill.AppStoreImporter>();

    // Backfill job tracking + the pluggable version-list adapters. The job store is the seam the
    // importers update; the adapters are keyed by Name so the list endpoint resolves one by route value.
    // The "scrape" client carries a real-browser UA (bare clients 403 on some sources).
    builder.Services.AddScoped<EggIncognito.Data.Services.BackfillJobStore>();
    builder.Services.AddScoped<EggIncognito.Data.Services.IBackfillJobStore>(
        sp => sp.GetRequiredService<EggIncognito.Data.Services.BackfillJobStore>());
    builder.Services.AddHttpClient("scrape", c => c.DefaultRequestHeaders.UserAgent.ParseAdd(
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36"));
    builder.Services.AddScoped<EggIncognito.Services.Backfill.Sources.FandomSource>();
    builder.Services.AddScoped<EggIncognito.Services.Backfill.Sources.UptodownSource>();
    builder.Services.AddScoped<EggIncognito.Services.Backfill.Sources.ApkPureSource>();
    builder.Services.AddScoped<EggIncognito.Services.Backfill.Sources.IApkDownloader>(
        sp => sp.GetRequiredService<EggIncognito.Services.Backfill.Sources.ApkPureSource>());
    builder.Services.AddScoped<EggIncognito.Services.Backfill.Sources.ItunesSource>();
    builder.Services.AddScoped<EggIncognito.Services.Backfill.Sources.Ipa4funSource>();
    builder.Services.AddScoped<EggIncognito.Services.Backfill.Sources.InternetArchiveSource>();
    builder.Services.AddKeyedScoped<EggIncognito.Services.Backfill.Sources.IVersionListSource>(
        "fandom", (sp, _) => sp.GetRequiredService<EggIncognito.Services.Backfill.Sources.FandomSource>());
    builder.Services.AddKeyedScoped<EggIncognito.Services.Backfill.Sources.IVersionListSource>(
        "uptodown", (sp, _) => sp.GetRequiredService<EggIncognito.Services.Backfill.Sources.UptodownSource>());
    builder.Services.AddKeyedScoped<EggIncognito.Services.Backfill.Sources.IVersionListSource>(
        "apkpure", (sp, _) => sp.GetRequiredService<EggIncognito.Services.Backfill.Sources.ApkPureSource>());
    builder.Services.AddKeyedScoped<EggIncognito.Services.Backfill.Sources.IVersionListSource>(
        "itunes", (sp, _) => sp.GetRequiredService<EggIncognito.Services.Backfill.Sources.ItunesSource>());
    builder.Services.AddKeyedScoped<EggIncognito.Services.Backfill.Sources.IVersionListSource>(
        "ipa4fun", (sp, _) => sp.GetRequiredService<EggIncognito.Services.Backfill.Sources.Ipa4funSource>());
    builder.Services.AddKeyedScoped<EggIncognito.Services.Backfill.Sources.IVersionListSource>(
        "archive", (sp, _) => sp.GetRequiredService<EggIncognito.Services.Backfill.Sources.InternetArchiveSource>());
    builder.Services.AddScoped<EggIncognito.Services.Backfill.VersionListImporter>();
    builder.Services.AddScoped<EggIncognito.Services.Backfill.ApkExtractService>();

    // Proactive store poller: a background timer that discovers new App Store / Play versions and queues
    // extraction. DB-gated (registered only here) + self-disables via VersionPoller:Enabled=false.
    var pollerOptions = EggIncognito.Services.Backfill.VersionPollerOptions.Bind(builder.Configuration);
    builder.Services.AddSingleton(pollerOptions);
    if (pollerOptions.Enabled)
        builder.Services.AddHostedService<EggIncognito.Services.Backfill.VersionPollerService>();
}

// Device polling: config-declared devices (adb serial / iOS UDID) probed on a schedule for the installed
// Egg Inc version. Empty config => the hosted service no-ops. Registered outside the DB block so the
// status panel + no-DB path work; the service itself DB-gates per-tick (no store => skip). ProcessRunner
// + TimeProvider are process-wide. TimeProvider may already be added by hosted capture below, so TryAdd.
var deviceConfig = EggIncognito.Services.Devices.DeviceConfig.Bind(builder.Configuration);
builder.Services.AddSingleton(deviceConfig);
builder.Services.AddSingleton<EggIncognito.Core.Services.Devices.IProcessRunner, EggIncognito.Core.Services.Devices.ProcessRunner>();
builder.Services.TryAddSingleton(TimeProvider.System);
if (deviceConfig.Enabled && deviceConfig.Devices.Count > 0)
    builder.Services.AddHostedService<EggIncognito.Services.Devices.DeviceProbeService>();

// Persistent per-device capture: one long-lived listener per device, the device's proxy pointed at it, its
// rinfo (authoritative iOS build) harvested onto disk. Gated by DeviceCapture:Enabled (default off); a
// device-farm-host capability independent of the public Hosted/Local gate. Registered always so the Save
// path + status panel can read the rinfo store even when disabled (it returns no listeners).
var deviceCaptureConfig = EggIncognito.Services.Devices.DeviceCaptureConfig.Bind(builder.Configuration);
builder.Services.AddSingleton(deviceCaptureConfig);
builder.Services.AddSingleton<EggIncognito.Core.Services.Devices.IDeviceProxyConfigurator,
    EggIncognito.Core.Services.Devices.AdbProxyConfigurator>();
builder.Services.AddSingleton<EggIncognito.Core.Services.Devices.IDeviceProxyConfigurator>(sp =>
    new EggIncognito.Core.Services.Devices.IosProxyConfigurator(
        sp.GetRequiredService<EggIncognito.Core.Services.Devices.IProcessRunner>(),
        new EggIncognito.Core.Services.Devices.IosProxyConfigurator.SshConfig(
            deviceCaptureConfig.IosSshHost, deviceCaptureConfig.IosSshPort, deviceCaptureConfig.IosSshKeyPath,
            deviceCaptureConfig.IosSetCommand, deviceCaptureConfig.IosClearCommand)));
builder.Services.AddSingleton(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var contentRoot = EggIncognito.Services.ContentRoot.Resolve(config["ContentRoot"]);
    var capturePath = config["CapturePath"] ?? Path.Combine(contentRoot, "captures");
    var caPath = config["CaPath"] ?? Path.Combine(capturePath, "eggincognito-ca.cer");
    return new EggIncognito.Services.Devices.DeviceCaptureManager(
        deviceCaptureConfig, deviceConfig, capturePath, caPath, proxyFactory: null, contentRoot,
        sp.GetRequiredService<ILogger<EggIncognito.Services.Devices.DeviceCaptureManager>>());
});
builder.Services.AddSingleton<EggIncognito.Services.Devices.DeviceProxyPusher>();
if (deviceCaptureConfig.Enabled && deviceConfig.Devices.Count > 0)
    builder.Services.AddHostedService(sp => sp.GetRequiredService<EggIncognito.Services.Devices.DeviceCaptureManager>());

// Per-device "check your own store for an update" (the manual Check button). Android drives the on-device
// Play Store via adb (drive command config-templated, default opens the store page); iOS fires the eggupdate
// tweak over ssh. Both re-read the installed version to report a verdict. The Android drive command + poll
// window come from DeviceCheck:Android:*; iOS reuses DeviceUpdate:Ios:* ssh config.
var androidDrive = builder.Configuration["DeviceCheck:Android:DriveCommand"]
    ?? "am start -a android.intent.action.VIEW -d market://details?id={package}";
var androidPollSeconds = builder.Configuration.GetValue("DeviceCheck:Android:PollSeconds", 15);
var androidPollAttempts = builder.Configuration.GetValue("DeviceCheck:Android:PollAttempts", 24);
builder.Services.AddSingleton<EggIncognito.Core.Services.Devices.IDeviceStoreChecker>(sp =>
    new EggIncognito.Services.Devices.AndroidPlayStoreChecker(
        sp.GetRequiredService<EggIncognito.Core.Services.Devices.IProcessRunner>(),
        new EggIncognito.Services.Devices.AndroidPlayStoreChecker.Options(androidDrive, androidPollSeconds, androidPollAttempts),
        sp.GetRequiredService<ILogger<EggIncognito.Services.Devices.AndroidPlayStoreChecker>>()));
// Tracks one in-flight check-update per device (in-memory). check-update runs the ~6-min store poll in the
// background and returns 202 at once; the UI polls GET check-status against this tracker. Singleton.
builder.Services.AddSingleton<EggIncognito.Services.Devices.IDeviceJobTracker,
    EggIncognito.Services.Devices.DeviceJobTracker>();
builder.Services.AddSingleton<EggIncognito.Core.Services.Devices.IDeviceStoreChecker>(sp =>
    new EggIncognito.Services.Devices.IosStoreChecker(
        sp.GetRequiredService<EggIncognito.Core.Services.Devices.IProcessRunner>(),
        sp.GetRequiredService<IConfiguration>(),
        sp.GetRequiredService<ILogger<EggIncognito.Services.Devices.IosStoreChecker>>()));

if (hostedCaptureOn)
{
    if (string.IsNullOrWhiteSpace(hostedCaptureOpts.AddressSecret))
        throw new InvalidOperationException("Capture:AddressSecret must be set when hosted capture is enabled (it is the HMAC key for per-user proxy addresses).");
    builder.Services.AddSingleton(sp =>
    {
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("capture.frontdoor");
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
        Func<System.Net.IPAddress, Task<string?>> addrToUser = async addr =>
        {
            using var scope = scopeFactory.CreateScope();
            var store = scope.ServiceProvider.GetService<EggIncognito.Data.Services.CaptureAddressStore>();
            return store is null ? null : await store.UserForAddrAsync(addr);
        };
        return new EggIncognito.Capture.ProxyFrontDoor(
            hostedCaptureOpts,
            sp.GetRequiredService<EggIncognito.Capture.CaptureSessionManager>(),
            addrToUser,
            msg => logger.LogInformation("{Message}", msg));
    });
    builder.Services.AddHostedService(sp => sp.GetRequiredService<EggIncognito.Capture.ProxyFrontDoor>());
    builder.Services.TryAddSingleton(TimeProvider.System);
    builder.Services.AddHostedService<CaptureSweeper>();
}

var app = builder.Build();

// Apply migrations + mirror yaml routes into stored_routes when a DB is configured. Fail fast on a
// broken DB in any mode: a hosted deploy must not silently serve files when its DB is misconfigured.
if (dbEnabled)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<EggIncognito.Data.Services.EggIncognitoDbContext>();
    await db.Database.MigrateAsync();
    await EggIncognito.Data.Services.RouteSeeder.SeedAsync(
        db, scope.ServiceProvider.GetRequiredService<RouteCatalog>());
    await EggIncognito.Data.Services.TagSeeder.SeedAsync(db);
    // Mirror config devices into the DB so the roster + probe history survive restarts. Config is
    // authoritative; a device dropped from config is disabled (not deleted), keeping probe-history FKs valid.
    {
        var deviceStore = scope.ServiceProvider.GetService<EggIncognito.Data.Services.IDeviceStatusStore>();
        if (deviceStore is not null)
        {
            var flat = deviceConfig.Devices
                .Select(d => (d.Id, d.Platform, d.Label, d.Target, d.Package)).ToList();
            await EggIncognito.Data.Services.DeviceSeeder.SeedAsync(deviceStore, db, flat);
        }
    }
    app.Logger.LogInformation("Postgres DB layer active: migrated + seeded yaml routes + tags.");
}
else
{
    app.Logger.LogInformation("No ConnectionStrings:Postgres - running file-only (no DB overlay).");
}

// Apply the proxy's forwarded headers FIRST so every downstream middleware (auth, routing, the OAuth
// challenge, link generation) sees the original https scheme + host, not the proxy's plain-http hop.
app.UseForwardedHeaders();

// App-wide structured error handling. Turns ApiException into {error, resolution, status} and any
// unhandled exception into a 500 that points at the logs.
app.UseExceptionHandler();

// protos.* host roots to the registry landing page: rewrite only the bare "/" path so the proto
// surface is the default there. Every other path (assets, /protos*, /api/protos*, the rest of the
// app) stays reachable from this host too; the main host is untouched. Runs before routing so the
// endpoint match sees the rewritten path.
app.Use(async (ctx, next) =>
{
    if (ctx.Request.Host.Host.StartsWith("protos.", StringComparison.OrdinalIgnoreCase)
        && ctx.Request.Path == "/")
    {
        ctx.Request.Path = "/protos";
    }
    await next();
});

// Static files must short-circuit before routing. SimulationController has a catch-all
// [HttpOptions("/{**slug}")] that otherwise makes every static GET report 405.
app.UseStaticFiles();

app.UseRouting();
if (authEnabled)
{
    app.UseAuthentication();
    app.UseAuthorization();
}
// Required by interactive Razor Components (the Blazor shell). Must sit after UseRouting + the auth
// middleware and before the endpoint maps. Static GETs are unaffected; only form-posting components
// validate the token.
app.UseAntiforgery();
app.UseRateLimiter();
app.MapControllers();
app.MapRazorComponents<EggIncognito.Components.App>()
   .AddInteractiveServerRenderMode();
app.MapGet("/health", () => Results.Ok());
// The UI fetches this on load to gate features. Capture/Import nav links + save/update buttons are
// hidden when the matching capability is off. Also carries auth state + current user so the SPA
// renders login state in one fetch.
app.MapGet("/api/app/mode", (IAppMode m, AuthState auth, ICurrentUser user) =>
    Results.Ok(new
    {
        mode = m.Mode.ToString(),
        canCapture = m.CanCapture,
        canWrite = m.CanWrite,
        hostedCapture = m.HostedCaptureEnabled,
        authEnabled = auth.Enabled,
        user = user.IsAuthenticated
            ? new { user.DiscordId, user.Username, user.Avatar,
                    role = EggIncognito.Data.Models.UserRoles.ToName(user.Role),
                    supporter = user.IsSupporter }
            : null,
    }));

// Re-checks the Supporter role and reissues the cookie with a fresh egi:supporter claim. Form-POST
// friendly: the /support page button posts here and the redirect lands back with the new cookie.
if (authEnabled)
{
    app.MapPost("/api/account/refresh-benefits",
        async (HttpContext http, ICurrentUser user, SupporterStatus checker) =>
    {
        if (!user.IsAuthenticated || string.IsNullOrEmpty(user.DiscordId))
            return Results.Unauthorized();
        var isSupporter = await checker.CheckAsync(user.DiscordId, http.RequestAborted);
        var identity = (System.Security.Claims.ClaimsIdentity)http.User.Identity!;
        SupporterClaims.Stamp(identity, isSupporter);
        await http.SignInAsync(
            Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme,
            new System.Security.Claims.ClaimsPrincipal(identity));
        return Results.Redirect("/support");
    }).RequireRateLimiting("read");
}

// Flush and close the per-startup log file cleanly on shutdown.
app.Lifetime.ApplicationStopping.Register(fileLogProvider.Dispose);

var signing = app.Services.GetRequiredService<ITransportPipeline>().CanSign;
app.Logger.LogInformation("WebRootPath = {WebRoot}", app.Environment.WebRootPath);
app.Logger.LogInformation("Request signing: {State} (EGG_INC_API_SALT {SaltState})",
    signing ? "ready" : "DISABLED", signing ? "set" : "not set");
app.Logger.LogInformation("Log file: {LogFile}", fileLogProvider.FilePath ?? "(file logging disabled)");

// In capture mode, start the proxy once the host is listening. Otherwise it is toggled at runtime
// via POST /api/capture/start.
if (captureMode)
{
    app.Lifetime.ApplicationStarted.Register(() =>
    {
        var sess = app.Services.GetRequiredService<EggIncognito.Capture.CaptureSession>();
        _ = sess.StartAsync(CancellationToken.None);
    });
}

// Auto-open the browser on startup: the Capture tab in capture mode, otherwise the Inspector. Plain
// `dotnet run` ignores launchSettings' launchBrowser, so open it ourselves. Development only,
// skippable with NoBrowser=true which Docker sets. Skipped under the test host too:
// WebApplicationFactory boots Program in Development but serves via TestServer, not Kestrel, so we
// must never spawn a real browser there.
var servesOverKestrel = app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>()
    .GetType().Name == "KestrelServer";
if (servesOverKestrel &&
    app.Environment.IsDevelopment() &&
    !app.Configuration.GetValue("NoBrowser", false))
{
    app.Lifetime.ApplicationStarted.Register(() =>
    {
        _ = Task.Run(async () =>
        {
            var addr = app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>()
                .Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()
                ?.Addresses.FirstOrDefault(a => a.StartsWith("http://"))
                ?? "http://localhost:5032";

            // Don't spawn a duplicate tab. A dashboard left open from a prior run reconnects over SSE
            // within ~1s of this process binding. Wait briefly; skip opening if a client already attached.
            if (captureMode)
            {
                await Task.Delay(TimeSpan.FromSeconds(1.5));
                var hub = app.Services.GetRequiredService<EggIncognito.Capture.CaptureSession>().Hub;
                if (hub.HasSubscribers)
                {
                    app.Logger.LogInformation("Dashboard already open (reconnected) - not opening a new tab.");
                    return;
                }
            }

            var url = addr.TrimEnd('/') + (captureMode ? "/capture" : "/inspector");
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                app.Logger.LogWarning(ex, "Could not auto-open browser at {Url}", url);
            }
        });
    });
}

await app.RunAsync();
return 0;
