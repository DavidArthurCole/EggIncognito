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

// Offline command: carve the .proto from a decrypted iOS Mach-O, Android APK, or a bare native .so
// (auto-detected) and exit. `dotnet run -- __extract-proto <binaryPath> <outPath>`.
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
        // No custom cert pair: Kestrel keeps its default endpoints, logged so a misplaced certs dir is diagnosable.
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
// API explorer powers the generic API console (/console): reflects every controller endpoint into a list.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
// TEMP diagnostic for silent StartCircuit failures - revert once real error is captured.
builder.Services.Configure<Microsoft.AspNetCore.SignalR.HubOptions>(o => o.EnableDetailedErrors = true);

// Behind the reverse proxy (Cloudflare -> origin nginx), TLS terminates at the edge, so the origin sees
// plain HTTP; honor X-Forwarded-Proto/-Host/-For so the app reconstructs the original https request.
// KnownProxies/KnownNetworks are cleared because the proxy is the sole ingress.
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
// Orbital-ship mesh downloader: fetches ship shells resolved from a DLCCatalog over the inspector egress client.
builder.Services.AddScoped<EggIncognito.Services.ShipShellDownloader>();
// On-disk decoded-mesh cache so device mesh requests serve a precomputed glb instead of re-pulling.
builder.Services.AddSingleton<EggIncognito.Services.MeshAssetCache>();
// Resolves a mesh stem to a glb by pulling it off a device and caching it (no shipped assets).
builder.Services.AddScoped<EggIncognito.Services.DeviceMeshProvider>();
// decomp constant extraction: pulls the egginc binary off the device for /api/decomp/*.
builder.Services.AddScoped<EggIncognito.Services.GameBinaryProvider>();
// Local copy of the game's per-platform *Config (ConfigResponse + DLCCatalog), feeds the shell viewer.
builder.Services.AddSingleton<EggIncognito.Services.GameConfigStore>();
// The "Sealed API proxy" supporter perk: a second inspector egress routed through a configured upstream
// so the downstream API cannot tie the request to this server. Unconfigured upstream = direct connection.
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

// Discord auth wires only when SyncKit.Identity is configured plus Discord creds are present. Authentik
// wires as a second, additive OIDC scheme when its own config keys are present; either can run standalone
// or both together. CurrentUser is always registered and reports anonymous when no auth middleware ran.
var identityApiUrl = builder.Configuration["Identity:ApiUrl"];
var identityApiSecret = builder.Configuration["Identity:ApiSecret"];
var identityApiEnabled = !string.IsNullOrWhiteSpace(identityApiUrl) && !string.IsNullOrWhiteSpace(identityApiSecret);
if (identityApiEnabled)
{
    builder.Services.AddHttpClient<SyncKit.Identity.Client.IdentityApiClient>(c =>
    {
        c.BaseAddress = new Uri(identityApiUrl!);
        c.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", identityApiSecret);
    });
}
var discordAuthEnabled = builder.AddDiscordAuthIfConfigured(identityApiEnabled);
var authentikAuthEnabled = builder.AddAuthentikAuthIfConfigured(identityApiEnabled);
if (authentikAuthEnabled)
{
    // Backs AuthController.BackchannelLogout's logout_token signature check: fetches and caches
    // Authentik's discovery doc/JWKS independently of the OIDC handler's own internal manager.
    var authority = builder.Configuration["Authentik:Authority"]!;
    builder.Services.AddSingleton(new Microsoft.IdentityModel.Protocols.ConfigurationManager<
        Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectConfiguration>(
        $"{authority.TrimEnd('/')}/.well-known/openid-configuration",
        new Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectConfigurationRetriever()));
}
var authEnabled = discordAuthEnabled || authentikAuthEnabled;
builder.Services.AddSingleton(new AuthState(discordAuthEnabled, authentikAuthEnabled, identityApiUrl));
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<EggIncognito.Services.Metrics.ApiMetrics>();
builder.Services.TryAddScoped<ICurrentUser, CurrentUser>();
// Supporter role check (login stamp + refresh-benefits). Always registered; without
// Discord:GuildId + Discord:SupporterRoleId + Discord:BotToken it short-circuits to false.
// Short timeout so a slow/rate-limited Discord cannot hang login; a timeout fails closed as "not a supporter".
builder.Services.AddHttpClient("discord-api", c => c.Timeout = TimeSpan.FromSeconds(8));
builder.Services.AddSingleton<SupporterStatus>();
builder.Services.AddSingleton<ISupporterStatus>(sp => sp.GetRequiredService<SupporterStatus>());
// Capture-CA Discord DM: the real REST notifier only when a bot token is configured, else a no-op so
// the web app carries no hard bot dependency.
if (!string.IsNullOrWhiteSpace(builder.Configuration["Discord:BotToken"]))
    builder.Services.AddSingleton<ICaptureCaNotifier, DiscordCaptureCaNotifier>();
else
    builder.Services.AddSingleton<ICaptureCaNotifier, NoopCaptureCaNotifier>();

// Discord bot: gateway presence + slash commands via SyncKit.Bot. Opt-in, only when Discord:BotToken
// is set. Reuses Discord:ClientId as the application id; optional Discord:GuildId scopes registration.
var botToken = builder.Configuration["Discord:BotToken"];
if (!string.IsNullOrWhiteSpace(botToken))
{
    const string repoUrl = "https://github.com/DavidArthurCole/EggIncognito";
    var buildInfo = EggIncognito.Services.BuildInfo.FromAssembly(repoUrl);
    var startedAt = DateTimeOffset.UtcNow;

    builder.Services.AddSingleton(repoUrl);
    builder.Services.AddSingleton<EggIncognito.Bot.IStatusProvider, EggIncognito.Services.StatusSnapshotFactory>();

    // IStatusProvider/IProtoReflection aren't resolvable until the service provider is built, so the
    // BotConfig singleton factory below resolves them lazily instead of capturing them directly.
    builder.Services.AddSingleton(sp =>
    {
        var status = sp.GetRequiredService<EggIncognito.Bot.IStatusProvider>();
        var proto = sp.GetRequiredService<EggIncognito.Services.IProtoReflection>();
        return new SyncKit.Bot.BotConfig
        {
            Name = "EggIncognito",
            Token = botToken,
            AppId = builder.Configuration["Discord:ClientId"] ?? "",
            GuildId = builder.Configuration["Discord:GuildId"] ?? "",
            RepoUrl = repoUrl,
            Build = new SyncKit.Contract.VerifyInfo
            {
                Name = "EggIncognito", Sha256 = buildInfo.Sha, Version = buildInfo.Version, Date = buildInfo.BuildDate,
            },
            // Shared with EggLedger in the same Portainer stack via the flat SHARED_ROLE_ID env var.
            SharedRoleId = builder.Configuration["SHARED_ROLE_ID"] ?? builder.Configuration["Discord:SharedRoleId"] ?? "",
            // /updateserver target: the host-side synckit-agent. Either missing means "not configured".
            DeployAgentUrl = builder.Configuration["DEPLOY_AGENT_URL"] ?? builder.Configuration["Discord:DeployAgentUrl"] ?? "",
            DeployAgentSecret = builder.Configuration["DEPLOY_AGENT_SECRET"] ?? builder.Configuration["Discord:DeployAgentSecret"] ?? "",
            PostgresConnectionString = dbEnabled ? pgConn! : "",
            DashboardChannelId = builder.Configuration["Discord:DashboardChannelId"] ?? "",
            EnabledThreads = builder.Configuration["Discord:EnabledThreads"] ?? "",
            GlobalCommands = true,
            Extra = new[]
            {
                EggIncognito.Bot.ExtraCommands.HealthCommand(startedAt),
                EggIncognito.Bot.ExtraCommands.StatusCommand(status),
                EggIncognito.Bot.ExtraCommands.EndpointsCommand(status),
                EggIncognito.Bot.ExtraCommands.ProtoCommand(proto),
            },
        };
    });
    builder.Services.AddSingleton<EggIncognito.Bot.EggIncognitoBotHostedService>();
    builder.Services.AddHostedService(sp => sp.GetRequiredService<EggIncognito.Bot.EggIncognitoBotHostedService>());
}

// Bot admin dashboard: /bot-admin config UI (SyncKit.Bot.AdminRoutes, vendored as BotAdminRoutes
// with paths remapped). Auth rides this app's own centralized login (ICurrentUser/UserRole.Admin),
// not a separate Discord OAuth flow - opt-in whenever Postgres + a bot token are configured.
var botAdminEnabled = !string.IsNullOrWhiteSpace(botToken) && dbEnabled;

// Inbound device-farm sync endpoint. Opt-in, only when SyncEvent:EventSecret is set; when absent,
// the route below is never mapped and requests 404. Also gated at runtime by IAppMode.
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
        // Expected proto identity, computed once from the frozen ei.proto, compared against each
        // event's protoSha to split regen-into-staged from flag-for-manual-refresh.
        var expectedProtoSha = EggIncognito.Core.ProtoHash.Current(syncContentRoot);
        var notifier = sp.GetRequiredService<EggIncognito.Bot.ISyncNotifier>();
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("sync.ingest");

        // Registry: upsert a proto_versions row for every build the farm reports, storing the .proto
        // text and parsed message index when present. No DB configured means no-op.
        async Task Registry(SyncKit.Contract.NewVersionEvent evt, CancellationToken ct)
        {
            using var scope = sp.CreateScope();
            var store = scope.ServiceProvider.GetService<EggIncognito.Data.Services.ProtoRegistryStore>();
            if (store is null) return;
            string? protoText = string.IsNullOrEmpty(evt.ProtoTextB64) ? null
                : System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(evt.ProtoTextB64));
            // appVersion and build fall back to the legacy single version when old emitters omit them.
            var appVersion = string.IsNullOrEmpty(evt.AppVersion) ? evt.Version : evt.AppVersion;
            var build = string.IsNullOrEmpty(evt.Build) ? evt.Version : evt.Build;
            if (string.IsNullOrEmpty(build) || string.IsNullOrEmpty(appVersion)) return;
            // SyncKit.Contract.NewVersionEvent.Platform has no "android" default like the old local DTO did.
            var platform = evt.Platform ?? "android";
            var (row, created, protoChanged) = await store.UpsertAsync(
                platform, appVersion, build, evt.ClientVersion, evt.Package, evt.ProtoSha, evt.ApkRef,
                DateTimeOffset.TryParse(evt.DetectedAt, out var dt) ? dt : DateTimeOffset.UtcNow,
                detectedBy: null, protoText, source: "farm", ct: ct);

            // Fan the event out to matching active subscriptions; best-effort and DB-gated.
            var dispatcher = scope.ServiceProvider.GetService<EggIncognito.Services.Feed.FeedDispatcher>();
            if (dispatcher is not null)
            {
                var cfg = scope.ServiceProvider.GetService<IConfiguration>();
                var pageUrl = EggIncognito.Services.Feed.FeedDispatcher.BuildPageUrl(
                    cfg?["Feed:PageBaseUrl"], platform, build);
                await dispatcher.DispatchAsync(row.Id, platform, appVersion, build, evt.ClientVersion,
                    evt.ProtoSha, created, protoChanged, pageUrl, ct);
            }
        }

        // Fetch apkRef under ApkFetchRoot; missing artifact is tolerated.
        Task Fetch(SyncKit.Contract.NewVersionEvent evt, CancellationToken ct)
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
        // HAR + capture routes use, never touching default/. Promotion stays a human step.
        Task Regen(SyncKit.Contract.NewVersionEvent evt, CancellationToken ct)
        {
            EndpointExtractor.ForRepo(syncContentRoot, eid: null, "EI0000000000000000", overwrite: true);
            logger.LogInformation("sync: staged area ready for {Version}; apk-driven regen not yet wired", evt.Version);
            return Task.CompletedTask;
        }

        // Stash: a changed proto is flagged, never auto-applied. Writes a small manifest recording
        // the version and the sha delta for the human gate.
        Task Stash(SyncKit.Contract.NewVersionEvent evt, CancellationToken ct)
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

// Per-user capture sessions. Local resolves the single anonymous LocalKey session through the manager.
var hostedCaptureOpts = EggIncognito.Capture.HostedCaptureOptions.Bind(builder.Configuration);
builder.Services.AddSingleton(hostedCaptureOpts);
builder.Services.AddSingleton(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    // Content root = the directory that directly holds RouteMap/ + Endpoints/: the project dir in dev,
    // the exe dir when published.
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
        // writes, no LAN forwarder and no OS trust-store install.
        var dir = Path.Combine(Path.GetTempPath(), "eggincognito-hosted-capture", key);
        var hostedOpts = new EggIncognito.Capture.CaptureSessionOptions(
            Port: basePort, Eid: null, Label: null, Overwrite: false,
            Verbose: config.GetValue("CaptureVerbose", false),
            CapturePath: dir, CaPath: Path.Combine(dir, "ca.cer"),
            WriteEndpoints: false);
        return new EggIncognito.Capture.CaptureSession(contentRoot, hostedOpts,
            verbose => new EggIncognito.Capture.NativeCaptureProxy(verbose)
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
    // Proto backfill importers (admin-triggered, on-demand); each importer opens its own DI scope inside
    // RunAsync. The "github" named client carries the optional GITHUB_TOKEN.
    builder.Services.AddScoped<EggIncognito.Data.Services.IProtoBackfillStore>(
        sp => sp.GetRequiredService<EggIncognito.Data.Services.ProtoRegistryStore>());
    builder.Services.AddHttpClient("github");
    builder.Services.AddScoped<EggIncognito.Services.Backfill.IGitHubClient, EggIncognito.Services.Backfill.GitHubClient>();
    builder.Services.AddScoped<EggIncognito.Services.Backfill.ElgranjeroImporter>();
    builder.Services.AddScoped<EggIncognito.Services.Backfill.PlayStoreImporter>();
    builder.Services.AddScoped<EggIncognito.Services.Backfill.AppStoreImporter>();

    // Backfill job tracking + the pluggable version-list adapters, keyed by Name so the list endpoint
    // resolves one by route value. The "scrape" client carries a real-browser UA since bare clients 403.
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
    // extraction. Self-disables via VersionPoller:Enabled=false.
    var pollerOptions = EggIncognito.Services.Backfill.VersionPollerOptions.Bind(builder.Configuration);
    builder.Services.AddSingleton(pollerOptions);
    if (pollerOptions.Enabled)
        builder.Services.AddHostedService<EggIncognito.Services.Backfill.VersionPollerService>();
}

// Device polling: config-declared devices (adb serial / iOS UDID) probed on a schedule for the installed
// Egg Inc version. Empty config means the hosted service no-ops. Registered outside the DB block so the
// status panel and no-DB path work; the service itself DB-gates per-tick.
var deviceConfig = EggIncognito.Services.Devices.DeviceConfig.Bind(builder.Configuration);
builder.Services.AddSingleton(deviceConfig);
builder.Services.AddSingleton<EggIncognito.Core.Services.Devices.IProcessRunner, EggIncognito.Core.Services.Devices.ProcessRunner>();
builder.Services.TryAddSingleton(TimeProvider.System);
if (deviceConfig.Enabled && deviceConfig.Devices.Count > 0)
    builder.Services.AddHostedService<EggIncognito.Services.Devices.DeviceProbeService>();

// Persistent per-device capture: one long-lived listener per device, the device's proxy pointed at it, its
// rinfo (authoritative iOS build) harvested onto disk. Gated by DeviceCapture:Enabled (default off), but
// registered always so the Save path and status panel can read the rinfo store even when disabled.
var deviceCaptureConfig = EggIncognito.Services.Devices.DeviceCaptureConfig.Bind(builder.Configuration);
builder.Services.AddSingleton(deviceCaptureConfig);
builder.Services.AddSingleton<EggIncognito.Core.Services.Devices.IDeviceProxyConfigurator,
    EggIncognito.Core.Services.Devices.AdbProxyConfigurator>();
builder.Services.AddSingleton<EggIncognito.Core.Services.Devices.IDeviceProxyConfigurator>(sp =>
    new EggIncognito.Core.Services.Devices.IosProxyConfigurator(
        sp.GetRequiredService<EggIncognito.Core.Services.Devices.IProcessRunner>(),
        new EggIncognito.Core.Services.Devices.IosProxyConfigurator.SshConfig(
            deviceCaptureConfig.IosSshHost, deviceCaptureConfig.IosSshPort, deviceCaptureConfig.IosSshKeyPath,
            deviceCaptureConfig.IosSetCommand, deviceCaptureConfig.IosClearCommand,
            deviceCaptureConfig.IosNetworkServiceGuid, deviceCaptureConfig.IosPlutilPath,
            deviceCaptureConfig.IosPreferencesPlist)));
// CA auto-install on the rooted/jailbroken farm devices: the capture CA is pushed and trusted on-device
// so the per-device proxy's MITM TLS decrypts. Android over adb, iOS over ssh (TrustStore.sqlite3 insert).
builder.Services.AddSingleton<EggIncognito.Core.Services.Devices.IDeviceCaInstaller>(sp =>
    new EggIncognito.Core.Services.Devices.AdbCaInstaller(
        sp.GetRequiredService<EggIncognito.Core.Services.Devices.IProcessRunner>(),
        deviceCaptureConfig.AndroidCaInstallScript));
builder.Services.AddSingleton<EggIncognito.Core.Services.Devices.IDeviceCaInstaller>(sp =>
    new EggIncognito.Core.Services.Devices.IosCaInstaller(
        sp.GetRequiredService<EggIncognito.Core.Services.Devices.IProcessRunner>(),
        new EggIncognito.Core.Services.Devices.IosCaInstaller.SshConfig(
            deviceCaptureConfig.IosSshHost, deviceCaptureConfig.IosSshPort, deviceCaptureConfig.IosSshKeyPath,
            deviceCaptureConfig.IosCaInstallCommand, deviceCaptureConfig.IosTrustStorePath)));
builder.Services.AddSingleton(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var contentRoot = EggIncognito.Services.ContentRoot.Resolve(config["ContentRoot"]);
    var capturePath = config["CapturePath"] ?? Path.Combine(contentRoot, "captures");
    var caPath = config["CaPath"] ?? Path.Combine(capturePath, "eggincognito-ca.cer");
    return new EggIncognito.Services.Devices.DeviceCaptureManager(
        deviceCaptureConfig, deviceConfig, capturePath, caPath, proxyFactory: null, contentRoot,
        sp.GetRequiredService<ILogger<EggIncognito.Services.Devices.DeviceCaptureManager>>(),
        sp.GetServices<EggIncognito.Core.Services.Devices.IDeviceCaInstaller>());
});
builder.Services.AddSingleton<EggIncognito.Services.Devices.DeviceProxyPusher>();
if (deviceCaptureConfig.Enabled && deviceConfig.Devices.Count > 0)
    builder.Services.AddHostedService(sp => sp.GetRequiredService<EggIncognito.Services.Devices.DeviceCaptureManager>());

// Per-device "check your own store for an update" (the manual Check button). Android drives the
// on-device Play Store via adb; iOS fires the eggupdate tweak over ssh. Both re-read the installed
// version to report a verdict.
var androidDrive = builder.Configuration["DeviceCheck:Android:DriveCommand"]
    ?? "am start -a android.intent.action.VIEW -d market://details?id={package}";
var androidPollSeconds = builder.Configuration.GetValue("DeviceCheck:Android:PollSeconds", 15);
var androidPollAttempts = builder.Configuration.GetValue("DeviceCheck:Android:PollAttempts", 24);
builder.Services.AddSingleton<EggIncognito.Core.Services.Devices.IDeviceStoreChecker>(sp =>
    new EggIncognito.Services.Devices.AndroidPlayStoreChecker(
        sp.GetRequiredService<EggIncognito.Core.Services.Devices.IProcessRunner>(),
        new EggIncognito.Services.Devices.AndroidPlayStoreChecker.Options(androidDrive, androidPollSeconds, androidPollAttempts),
        sp.GetRequiredService<ILogger<EggIncognito.Services.Devices.AndroidPlayStoreChecker>>()));
// Tracks one in-flight check-update per device (in-memory). check-update runs the ~6-min store poll in
// the background and returns 202 at once; the UI polls GET check-status against this tracker.
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

// Apply migrations and mirror yaml routes into stored_routes when a DB is configured. Fails fast on a
// broken DB so a hosted deploy never silently serves files when its DB is misconfigured.
if (dbEnabled)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<EggIncognito.Data.Services.EggIncognitoDbContext>();
    await db.Database.MigrateAsync();
    await EggIncognito.Data.Services.RouteSeeder.SeedAsync(
        db, scope.ServiceProvider.GetRequiredService<RouteCatalog>());
    await EggIncognito.Data.Services.TagSeeder.SeedAsync(db);
    // Mirror config devices into the DB so the roster and probe history survive restarts. Config is
    // authoritative; a device dropped from config is disabled, not deleted, keeping probe-history FKs valid.
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

// Apply the proxy's forwarded headers first so every downstream middleware sees the original https
// scheme and host, not the proxy's plain-http hop.
app.UseForwardedHeaders();

// Turns ApiException into {error, resolution, status} and any unhandled exception into a 500 that
// points at the logs.
app.UseExceptionHandler();

// protos.* host roots to the registry landing page: rewrite only the bare "/" path so the proto
// surface is the default there. Runs before routing so the endpoint match sees the rewritten path.
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
// Required by interactive Razor Components. Must sit after UseRouting + the auth middleware and before
// the endpoint maps; only form-posting components validate the token.
app.UseAntiforgery();
app.UseRateLimiter();

// API-rate metrics: counts every /api request and flags 429s into the in-process ring. After UseRateLimiter
// so a rejected request's 429 status is observable here.
{
    var metrics = app.Services.GetRequiredService<EggIncognito.Services.Metrics.ApiMetrics>();
    app.Use(async (ctx, next) =>
    {
        var isApi = ctx.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase);
        await next();
        if (isApi) metrics.Record(limited: ctx.Response.StatusCode == StatusCodes.Status429TooManyRequests);
    });
}

app.MapControllers();
if (!string.IsNullOrWhiteSpace(eventSecret))
{
    var ingest = app.Services.GetRequiredService<EggIncognito.Services.NewVersionIngestService>();
    app.MapPost("/events/new-version", SyncKit.Bot.NewVersionHandler.Build(eventSecret, evt => ingest.HandleAsync(evt)))
        .RequireRateLimiting("write");
}
// Migrations run whenever BotConfig.PostgresConnectionString is set (i.e. bot token + Postgres both
// configured), independent of the admin-UI OAuth gate below: SyncKitBot's channel hub touches
// bot_channel_config/bot_channel_state as soon as a Postgres connection string is present, whether
// or not the /bot-admin page itself is enabled.
if (!string.IsNullOrWhiteSpace(botToken) && dbEnabled)
{
    await using var adminConn = await Npgsql.NpgsqlDataSource.Create(pgConn!).OpenConnectionAsync();
    await SyncKit.Db.Migrator.MigrateAsync(adminConn, Path.Combine(AppContext.BaseDirectory, "Migrations"));
}
if (botAdminEnabled)
{
    var adminDataSource = Npgsql.NpgsqlDataSource.Create(pgConn!);
    var configStore = new SyncKit.Bot.ChannelConfigStore(adminDataSource);
    var botCfg = app.Services.GetRequiredService<SyncKit.Bot.BotConfig>();
    bool IsAdmin(HttpContext ctx) =>
        ctx.RequestServices.GetRequiredService<ICurrentUser>().IsAtLeast(EggIncognito.Data.Models.UserRole.Admin);
    EggIncognito.Bot.BotAdminRoutes.Map(app, botCfg, configStore, IsAdmin);
}
app.MapRazorComponents<EggIncognito.Components.App>()
   .AddInteractiveServerRenderMode();
app.MapGet("/health", () => Results.Ok());
// The UI fetches this on load to gate features. Capture/Import nav links and save/update buttons are
// hidden when the matching capability is off; also carries auth state and current user.
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

// Re-checks the Supporter role and reissues the cookie with a fresh egi:supporter claim; the /support
// page button posts here and the redirect lands back with the new cookie.
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

// In capture mode, start the proxy once the host is listening; otherwise it is toggled at runtime
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
// `dotnet run` ignores launchSettings' launchBrowser, so open it ourselves. Development only, skippable
// with NoBrowser=true (Docker sets it), and skipped under the test host since WebApplicationFactory
// serves via TestServer, not Kestrel.
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

            // Don't spawn a duplicate tab: a dashboard left open from a prior run reconnects over SSE
            // within ~1s of this process binding, so wait briefly and skip if a client already attached.
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
