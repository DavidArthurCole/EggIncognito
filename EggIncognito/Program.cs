using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using EggIdentity.Auth;
using EggIdentity.Bot;
using EggIdentity.Client;
using EggIdentity.Contract;
using EggIdentity.Db;
using EggIdentity.Metrics;
using EggIdentity.Metrics.AdminUi;
using EggIncognito.Bot;
using EggIncognito.Build;
using EggIncognito.Capture;
using EggIncognito.Components;
using EggIncognito.Core;
using EggIncognito.Core.Services.Assets;
using EggIncognito.Core.Services.Devices;
using EggIncognito.Data.Services;
using EggIncognito.Logging;
using EggIncognito.Services;
using EggIncognito.Services.Assets;
using EggIncognito.Services.Auth;
using EggIncognito.Services.DataApi;
using EggIncognito.Services.Devices;
using EggIncognito.Services.Feed;
using EggIncognito.Services.Metrics;
using EggIncognito.Services.Protos;
using EggIncognito.Services.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

[assembly: InternalsVisibleTo("EggIncognito.Tests")]

if (args.Length >= 3 && args[0] is "__extract-proto" or "__extract-ios-proto")
    return IosProtoExtractor.Run(args[1], args[2]);

bool captureMode = args.Contains("--capture");
if (captureMode) {
    string? ArgValue(string name) {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    string? eid = ArgValue("--eid");
    string? label = ArgValue("--label");
    if (eid is not null) Environment.SetEnvironmentVariable("EGG_INC_EID", eid);
    if (label is not null) Environment.SetEnvironmentVariable("CaptureLabel", label);
    if (args.Contains("--overwrite")) Environment.SetEnvironmentVariable("CaptureOverwrite", "true");
}

var builder = WebApplication.CreateBuilder(args);

if (Environment.GetEnvironmentVariable("EGGINCOGNITO_TEST_DBFREE") == "1"
    && string.IsNullOrEmpty(builder.Configuration["TestDbOptIn"])) {
    builder.Configuration["ConnectionStrings:Postgres"] = "";
}

string logsDir = builder.Configuration["LogsPath"]
                 ?? Path.Combine(AppContext.BaseDirectory, "logs");
string startupStamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
var fileLogProvider = new FileLoggerProvider(logsDir, startupStamp);
builder.Logging.AddProvider(fileLogProvider);

builder.WebHost.ConfigureKestrel((context, opts) => {
    string certsPath = context.Configuration["CertsPath"]
                       ?? Path.Combine(AppContext.BaseDirectory, "certs");
    string certFile = Path.Combine(certsPath, "server.crt");
    string keyFile = Path.Combine(certsPath, "server.key");
    if (!File.Exists(certFile) || !File.Exists(keyFile)) {
        opts.ApplicationServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("Startup")
            .LogWarning(
                "No TLS cert pair at {CertsPath} (server.crt + server.key) - custom HTTP/HTTPS ports not bound, using default endpoints.",
                certsPath);
        return;
    }

    int httpPort = int.TryParse(context.Configuration["HttpPort"], out int hp) ? hp : 8080;
    int httpsPort = int.TryParse(context.Configuration["HttpsPort"], out int sp) ? sp : 8443;
    opts.ListenAnyIP(httpPort);
    opts.ListenAnyIP(httpsPort, o => o.UseHttps(X509Certificate2.CreateFromPemFile(certFile, keyFile)));
});

builder.Services.AddControllers(o => o.Filters.Add<ApiAccessFilter>());
builder.Services.Configure<RouteOptions>(o => o.ConstraintMap.Add("eins", typeof(EiNamespaceRouteConstraint)));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddRazorComponents().AddInteractiveServerComponents()
    .AddHubOptions(o => o.MaximumReceiveMessageSize = 2 * 1024 * 1024);


builder.Services.Configure<ForwardedHeadersOptions>(o => {
    o.ForwardedHeaders = ForwardedHeaders.XForwardedProto
                         | ForwardedHeaders.XForwardedHost
                         | ForwardedHeaders.XForwardedFor;
    o.KnownProxies.Clear();
    o.KnownIPNetworks.Clear();
});
builder.Services.AddAppRateLimiter(builder.Configuration);
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddHttpClient("inspector", c => {
    c.DefaultRequestHeaders.Add("User-Agent",
        "Dalvik/2.1.0 (Linux; U; Android 9; SM-G960U1 Build/PPR1.180610.011)");
    c.DefaultRequestHeaders.Add("Accept-Encoding", "gzip");
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler {
    AutomaticDecompression = DecompressionMethods.GZip
});

builder.Services.AddScoped<ShipShellDownloader>();

builder.Services.AddSingleton<MeshAssetCache>();
builder.Services.AddSingleton<IconAssetCache>();

builder.Services.AddScoped<IDeviceResolver, DeviceResolver>();

builder.Services.AddScoped<IGameAssetTier, MeshDbTier>();
builder.Services.AddScoped<IGameAssetTier, MeshDiskTier>();
builder.Services.AddScoped<IGameAssetTier, ConfigDiskTier>();
builder.Services.AddScoped<IGameAssetTier, IconDbTier>();
builder.Services.AddScoped<IGameAssetTier, IconDiskTier>();
builder.Services.AddScoped<IGameAssetOrigin, IconCdnOrigin>();
builder.Services.AddScoped<GameAssetProvider>();

builder.Services.AddScoped<DeviceMeshProvider>();

builder.Services.AddScoped<GameBinaryProvider>();
builder.Services.AddScoped<GameDataRebuilder>();
builder.Services.AddScoped<EndpointCatalogRebuilder>();

builder.Services.AddSingleton<GameConfigStore>();
builder.Services.AddSingleton<PeriodicalsChangeNotifier>();
builder.Services.AddSingleton<DataCatalog>();
builder.Services.AddSingleton<ConfigSliceCache>();
var sealedProxyOptions = SealedProxyOptions.FromConfig(builder.Configuration);
builder.Services.AddSingleton(sealedProxyOptions);
builder.Services.AddSingleton<ISealedProxy, SealedProxy>();
builder.Services.AddHttpClient(SealedProxy.EgressClientName, c => {
    c.DefaultRequestHeaders.Add("User-Agent",
        "Dalvik/2.1.0 (Linux; U; Android 9; SM-G960U1 Build/PPR1.180610.011)");
    c.DefaultRequestHeaders.Add("Accept-Encoding", "gzip");
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler {
    AutomaticDecompression = DecompressionMethods.GZip,
    Proxy = SealedProxy.BuildProxy(sealedProxyOptions),
    UseProxy = SealedProxy.BuildProxy(sealedProxyOptions) is not null
});
builder.Services.AddSingleton<IAppMode, AppModeService>();
builder.Services.AddSingleton<IBehaviorService, BehaviorService>();
builder.Services.AddSingleton<IProtoReflection, ProtoReflection>();
builder.Services.AddSingleton<GameDataStore>();
builder.Services.AddSingleton<FarmPlacementDataProvider>();
builder.Services.AddSingleton<IDocRegistry, DocRegistry>();
builder.Services.AddSingleton<ILastKnownProtoSource, LastKnownProtoSource>();
builder.Services.AddSingleton<IEnumFailover, EnumFailover>();
builder.Services.AddSingleton<ITransportPipeline, TransportPipeline>();

string? pgConn = builder.Configuration.GetConnectionString("Postgres");
bool dbEnabled = !string.IsNullOrWhiteSpace(pgConn);
builder.Services.AddSingleton(sp => {
    var config = sp.GetRequiredService<IConfiguration>();
    string path = config["EndpointsPath"] ?? Path.Combine(AppContext.BaseDirectory, "Endpoints");
    return new FileEndpointSource(path);
});
builder.Services.AddSingleton<IEndpointStore>(sp => {
    var logger = sp.GetRequiredService<ILogger<EndpointStore>>();
    var fileSource = sp.GetRequiredService<FileEndpointSource>();
    var scopeFactory = dbEnabled ? sp.GetRequiredService<IServiceScopeFactory>() : null;
    return new EndpointStore(fileSource, scopeFactory, logger);
});

builder.Services.AddSingleton<RouteCatalog>();
builder.Services.AddSingleton<AuxbrainSurface>();
builder.Services.AddSingleton<IRouteCatalog>(sp =>
    new OverlayRouteCatalog(
        new MergedRouteCatalog(
            sp.GetRequiredService<RouteCatalog>(),
            dbEnabled ? sp.GetRequiredService<IDbRouteProvider>() : null,
            dbEnabled ? sp.GetRequiredService<IBinaryRouteProvider>() : null),
        dbEnabled ? sp.GetRequiredService<IRouteOverrideProvider>() : null));

if (dbEnabled) {
    builder.Services.AddDbContextPool<EggIncognitoDbContext>(o => o.UseNpgsql(pgConn));


    builder.Services.AddDataProtection()
        .SetApplicationName("EggIncognito")
        .PersistKeysToDbContext<EggIncognitoDbContext>();

    builder.Services.AddScoped<GameBinaryStore>();
    builder.Services.AddScoped<DeviceAssetStore>();
    builder.Services.AddScoped<DeviceStateStore>();
    builder.Services.AddScoped<DbEndpointSource>();
    builder.Services.AddScoped(sp =>
        new DbEndpointSourceMarker(sp.GetRequiredService<DbEndpointSource>()));


    builder.Services.AddScoped<DbRouteProvider>();
    builder.Services.AddSingleton<IDbRouteProvider>(sp =>
        new CachedDbRouteProvider(
            new ScopedDbRouteProvider(sp.GetRequiredService<IServiceScopeFactory>()),
            TimeSpan.FromSeconds(15)));

    builder.Services.AddScoped<BinaryRouteProvider>();
    builder.Services.AddSingleton<IBinaryRouteProvider>(sp =>
        new CachedBinaryRouteProvider(
            new ScopedBinaryRouteProvider(sp.GetRequiredService<IServiceScopeFactory>()),
            TimeSpan.FromSeconds(15)));

    builder.Services.AddSingleton<IRouteOverrideProvider>(sp =>
        new CachedRouteOverrideProvider(
            () => RouteOverrideFetch.All(sp.GetRequiredService<IServiceScopeFactory>()),
            TimeSpan.FromSeconds(15)));
}

string? identityApiUrl = builder.Configuration[IdentityConfigKeys.ApiUrl];
string? identityApiSecret = builder.Configuration[IdentityConfigKeys.ApiSecret];

string? identityWidgetUrl = builder.Configuration[IdentityConfigKeys.WidgetUrl];
bool identityApiEnabled = !string.IsNullOrWhiteSpace(identityApiUrl) && !string.IsNullOrWhiteSpace(identityApiSecret);
if (identityApiEnabled) {
    builder.Services.AddHttpClient<IdentityApiClient>(c => {
        c.BaseAddress = new Uri(identityApiUrl!);
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", identityApiSecret);
    });
}

var eggIdentitySession = SessionCookieOptions.FromEnvironment();
if (eggIdentitySession is not null) builder.Services.AddSingleton(eggIdentitySession);
builder.AddEggIdentityAuthIfConfigured(identityApiEnabled, eggIdentitySession);
var authState = new AuthState(identityApiEnabled, identityWidgetUrl, eggIdentitySession?.CookieName ?? "eggidentity_session");
bool authEnabled = authState.Enabled;
builder.Services.AddSingleton(authState);
builder.Services.AddScoped<LoginSignIn>();
builder.Services.AddHttpContextAccessor();
bool hostedBehindProxy = string.Equals(builder.Configuration["AppMode"], "Hosted", StringComparison.OrdinalIgnoreCase);
builder.Services.AddEggIdentityRequestMetrics(o => {
    o.PathPrefix = "/api";
    o.InternalMarkerHeader = SelfCallClient.InternalMarkerHeader;
    o.HostedBehindProxy = hostedBehindProxy;
});
builder.Services.AddSingleton<ITrafficSource, TrafficSource>();
builder.Services.TryAddScoped<ICurrentUser, CurrentUser>();

builder.Services.AddHttpClient("discord-api", c => c.Timeout = TimeSpan.FromSeconds(8));
builder.Services.AddSingleton<SupporterStatus>();
builder.Services.AddSingleton<ISupporterStatus>(sp => sp.GetRequiredService<SupporterStatus>());
if (!string.IsNullOrWhiteSpace(builder.Configuration["Discord:BotToken"]))
    builder.Services.AddSingleton<ICaptureCaNotifier, DiscordCaptureCaNotifier>();
else
    builder.Services.AddSingleton<ICaptureCaNotifier, NoopCaptureCaNotifier>();

string? botToken = builder.Configuration["Discord:BotToken"];
if (!string.IsNullOrWhiteSpace(botToken)) {
    const string repoUrl = "https://github.com/DavidArthurCole/EggIncognito";
    var buildInfo = BuildInfo.FromAssembly(repoUrl);
    var startedAt = DateTimeOffset.UtcNow;

    builder.Services.AddSingleton(new RepoUrl(repoUrl));
    builder.Services.AddSingleton<IStatusProvider, StatusSnapshotFactory>();


    builder.Services.AddSingleton(sp => {
        var status = sp.GetRequiredService<IStatusProvider>();
        var proto = sp.GetRequiredService<IProtoReflection>();
        return new BotConfig {
            Name = "EggIncognito",
            Token = botToken,
            AppId = builder.Configuration["Discord:ClientId"] ?? "",
            GuildId = builder.Configuration["Discord:GuildId"] ?? "",
            RepoUrl = repoUrl,
            Build = new VerifyInfo {
                Name = "EggIncognito",
                Sha256 = buildInfo.Sha,
                Version = buildInfo.Version,
                Date = buildInfo.BuildDate
            },

            SharedRoleId = builder.Configuration["SHARED_ROLE_ID"] ??
                           builder.Configuration["Discord:SharedRoleId"] ?? "",

            DeployAgentUrl = builder.Configuration["DEPLOY_AGENT_URL"] ??
                             builder.Configuration["Discord:DeployAgentUrl"] ?? "",
            DeployAgentSecret = builder.Configuration["DEPLOY_AGENT_SECRET"] ??
                                builder.Configuration["Discord:DeployAgentSecret"] ?? "",
            PostgresConnectionString = dbEnabled ? pgConn! : "",
            DashboardChannelId = builder.Configuration["Discord:DashboardChannelId"] ?? "",
            DashboardProvider = _ => Task.FromResult(DashboardSnapshotFor(status, buildInfo, startedAt, repoUrl)),
            DashboardRefreshInterval = TimeSpan.FromMinutes(5),
            GlobalCommands = true,
            Extra = new[] {
                ExtraCommands.HealthCommand(startedAt),
                ExtraCommands.StatusCommand(status),
                ExtraCommands.EndpointsCommand(status),
                ExtraCommands.ProtoCommand(proto)
            }
        };
    });
    builder.Services.AddSingleton<EggIncognitoBotHostedService>();
    builder.Services.AddHostedService(sp => sp.GetRequiredService<EggIncognitoBotHostedService>());


    builder.Services.AddScoped(sp =>
        sp.GetRequiredService<EggIncognitoBotHostedService>().Bot?.ConfigService!);

    static DashboardSnapshot DashboardSnapshotFor(
        IStatusProvider status,
        BuildInfo buildInfo,
        DateTimeOffset startedAt,
        string repoUrl) {
        var snap = new DashboardSnapshot {
            AppName = "EggIncognito",
            Version = buildInfo.Version,
            BuildHash = buildInfo.Sha,
            DeployStatus = "online",
            UptimeSince = startedAt,
            RepoUrl = repoUrl
        };
        try {
            var s = status.Build();
            snap.ExtraFields = new Dictionary<string, string> {
                ["Mode"] = s.Mode,
                ["Devices"] = s.DeviceCount.ToString(CultureInfo.InvariantCulture),
                ["Capture"] = s.CaptureState,
                ["DB"] = s.DbEnabled ? "on" : "off",
                ["Signing"] = s.SigningReady ? "ready" : "unset"
            };
        } catch {
        }

        return snap;
    }
}

string? eventSecret = builder.Configuration["SyncEvent:EventSecret"];
if (!string.IsNullOrWhiteSpace(eventSecret)) {
    string syncContentRoot = ContentRoot.Resolve(builder.Configuration["ContentRoot"]);
    var syncOptions = new SyncEventOptions {
        EventSecret = eventSecret,
        ApkFetchRoot = builder.Configuration["SyncEvent:ApkFetchRoot"] ?? ""
    };
    builder.Services.AddSingleton(syncOptions);
    builder.Services.AddSingleton<ISyncNotifier, DiscordSyncNotifier>();
    builder.Services.AddSingleton(sp => {
        string expectedProtoSha = ProtoHash.Current();
        var notifier = sp.GetRequiredService<ISyncNotifier>();
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("sync.ingest");


        async Task Registry(NewVersionEvent evt, CancellationToken ct) {
            using var scope = sp.CreateScope();
            var store = scope.ServiceProvider.GetService<ProtoRegistryStore>();
            if (store is null) return;
            string? protoText = string.IsNullOrEmpty(evt.ProtoTextB64)
                ? null
                : Encoding.UTF8.GetString(Convert.FromBase64String(evt.ProtoTextB64));
            string protoSha = evt.ProtoSha;
            if (protoText is not null) {
                var norm = EggIncognito.Services.ProtoExtract.ProtoCanonicalForm.Normalize(protoText);
                if (norm.Ok) {
                    protoText = norm.Text!;
                    protoSha = norm.Sha!;
                }
            }

            string? appVersion = string.IsNullOrEmpty(evt.AppVersion) ? evt.Version : evt.AppVersion;
            string? build = string.IsNullOrEmpty(evt.Build) ? evt.Version : evt.Build;
            if (string.IsNullOrEmpty(build) || string.IsNullOrEmpty(appVersion)) return;

            string platform = evt.Platform ?? "android";
            (var row, bool created, bool protoChanged) = await store.UpsertAsync(
                platform, appVersion, build, evt.ClientVersion, evt.Package, protoSha, evt.ApkRef,
                DateTimeOffset.TryParse(evt.DetectedAt, out var dt) ? dt : DateTimeOffset.UtcNow,
                null, protoText, ct: ct);


            var dispatcher = scope.ServiceProvider.GetService<FeedDispatcher>();
            if (dispatcher is not null) {
                var cfg = scope.ServiceProvider.GetService<IConfiguration>();
                string pageUrl = FeedDispatcher.BuildPageUrl(
                    cfg?["Feed:PageBaseUrl"], platform, build);
                await dispatcher.DispatchAsync(new ProtoBuildEvent(
                    row.Id, platform, appVersion, build, evt.ClientVersion,
                    protoSha, created, protoChanged, pageUrl), ct);
            }
        }


        Task Fetch(NewVersionEvent evt, CancellationToken ct) {
            if (string.IsNullOrEmpty(syncOptions.ApkFetchRoot) || string.IsNullOrEmpty(evt.ApkRef)) {
                logger.LogInformation("sync: no ApkFetchRoot or apkRef for {Version}, skipping fetch", evt.Version);
                return Task.CompletedTask;
            }

            string apk = Path.Combine(syncOptions.ApkFetchRoot, evt.ApkRef.TrimStart('/', '\\'));
            if (!File.Exists(apk))
                logger.LogWarning("sync: apk not found at {Apk} for {Version}", apk, evt.Version);
            return Task.CompletedTask;
        }


        Task Regen(NewVersionEvent evt, CancellationToken ct) {
            EndpointExtractor.ForRepo(syncContentRoot, null, "EI0000000000000000", true);
            logger.LogInformation("sync: staged area ready for {Version}; apk-driven regen not yet wired", evt.Version);
            return Task.CompletedTask;
        }


        Task Stash(NewVersionEvent evt, CancellationToken ct) {
            string stashDir = Path.Combine(syncContentRoot, "Endpoints", "staged", "proto-refresh");
            Directory.CreateDirectory(stashDir);
            string manifest = JsonSerializer.Serialize(new {
                version = evt.Version,
                oldProtoSha = expectedProtoSha,
                newProtoSha = evt.ProtoSha,
                apkRef = evt.ApkRef,
                detectedAt = evt.DetectedAt
            });
            File.WriteAllText(Path.Combine(stashDir, $"{evt.Version}.json"), manifest);
            logger.LogWarning("sync: proto changed for {Version}, stashed refresh manifest", evt.Version);
            return Task.CompletedTask;
        }

        return new NewVersionIngestService(expectedProtoSha, notifier, Registry, Fetch, Regen, Stash);
    });
}

var hostedCaptureOpts = HostedCaptureOptions.Bind(builder.Configuration);
builder.Services.AddSingleton(hostedCaptureOpts);
builder.Services.AddSingleton(sp => {
    var config = sp.GetRequiredService<IConfiguration>();


    string contentRoot = ContentRoot.Resolve(config["ContentRoot"]);
    var routeCatalog = sp.GetRequiredService<IRouteCatalog>();
    return new CaptureSessionManager(hostedCaptureOpts, (key, basePort) => {
        var liveRoutes = sp.GetRequiredService<DataCatalog>().WireRoutes();
        var writeObserver = sp.GetService<PeriodicalsChangeNotifier>();
        if (key == CaptureSessionManager.LocalKey) {
            string capturePath = config["CapturePath"] ?? Path.Combine(contentRoot, "captures");
            string caPath = config["CaPath"] ?? Path.Combine(capturePath, "eggincognito-ca.cer");
            var opts = new CaptureSessionOptions(
                int.TryParse(config["CapturePort"], out int cp) ? cp : 8080,
                config["EGG_INC_EID"] ?? Environment.GetEnvironmentVariable("EGG_INC_EID"),
                config["CaptureLabel"],
                config.GetValue("CaptureOverwrite", false),
                config.GetValue("CaptureVerbose", false),
                capturePath,
                caPath,
                WriteObserver: writeObserver) { LiveRoutes = liveRoutes };
            return new CaptureSession(contentRoot, opts, catalog: routeCatalog);
        }


        string dir = Path.Combine(Path.GetTempPath(), "eggincognito-hosted-capture", key);
        var hostedOpts = new CaptureSessionOptions(
            basePort, null, null, false,
            config.GetValue("CaptureVerbose", false),
            dir, Path.Combine(dir, "ca.cer"),
            false,
            writeObserver) { LiveRoutes = liveRoutes };
        return new CaptureSession(contentRoot, hostedOpts,
            verbose => new NativeCaptureProxy(verbose) {
                LanForwarderEnabled = false,
                TrustCaInOsStore = false
            }, routeCatalog);
    });
});
builder.Services.AddSingleton(sp =>
    sp.GetRequiredService<CaptureSessionManager>()
        .GetOrCreate(CaptureSessionManager.LocalKey));

bool hostedCaptureOn = string.Equals(builder.Configuration["AppMode"], "Hosted", StringComparison.OrdinalIgnoreCase)
                       && builder.Configuration.GetValue("HostedCaptureEnabled", false);
if (dbEnabled) {
    builder.Services.AddScoped<CaptureCredentialStore>();
    builder.Services.AddScoped<CaptureAddressStore>();
    builder.Services.AddScoped<ProtoRegistryStore>();
    builder.Services.AddScoped<StagedProtoStore>();
    builder.Services.AddScoped<AnalyzedFileStore>();
    builder.Services.AddScoped<DeviceStatusStore>();
    builder.Services.AddScoped<IDeviceStatusStore>(sp => sp.GetRequiredService<DeviceStatusStore>());
    builder.Services.AddScoped<FeedSubscriptionStore>();
    builder.Services.AddScoped<IFeedSubscriptionStore>(sp => sp.GetRequiredService<FeedSubscriptionStore>());
    builder.Services.AddScoped<FeedDispatcher>();
    builder.Services.AddScoped<ApiKeyStore>();


    builder.Services.AddScoped<IProtoBackfillStore>(sp => sp.GetRequiredService<ProtoRegistryStore>());
}


var deviceConfig = DeviceConfig.Bind(builder.Configuration);
builder.Services.AddSingleton(deviceConfig);
var probeTimeoutSeconds = builder.Configuration.GetValue("DeviceProbe:TimeoutSeconds", 0);
if (probeTimeoutSeconds > 0)
    DeviceProbeTimeout.Value = TimeSpan.FromSeconds(probeTimeoutSeconds);
builder.Services.AddSingleton<IProcessRunner, ProcessRunner>();
builder.Services.AddScoped<AnalysisWorkbenchState>();
builder.Services.TryAddSingleton(TimeProvider.System);
builder.Services.AddHttpClient<IDeviceAgentClient, DeviceAgentClient>();
if (deviceConfig.Enabled && deviceConfig.Devices.Count > 0)
    builder.Services.AddHostedService<DeviceMaintenanceService>();
if (dbEnabled)
    builder.Services.AddHostedService<GameDataAutoRebuildService>();
if (dbEnabled)
    builder.Services.AddHostedService<EndpointCatalogAutoRefreshService>();


var deviceCaptureConfig = DeviceCaptureConfig.Bind(builder.Configuration);
builder.Services.AddSingleton(deviceCaptureConfig);
builder.Services.AddSingleton<IDeviceConnectionFactory,
    DeviceConnectionFactory>();
builder.Services.AddSingleton<IDeviceProxyConfigurator,
    AdbProxyConfigurator>();
builder.Services.AddSingleton<IDeviceProxyConfigurator>(sp =>
    new IosProxyConfigurator(
        sp.GetRequiredService<IProcessRunner>(),
        new IosProxyConfigurator.SshConfig(
            deviceCaptureConfig.IosSshHost, deviceCaptureConfig.IosSshPort, deviceCaptureConfig.IosSshKeyPath,
            deviceCaptureConfig.IosSetCommand, deviceCaptureConfig.IosClearCommand,
            deviceCaptureConfig.IosNetworkServiceGuid, deviceCaptureConfig.IosPlutilPath,
            deviceCaptureConfig.IosPreferencesPlist)));
builder.Services.AddSingleton<IDeviceCaInstaller>(sp =>
    new AdbCaInstaller(
        sp.GetRequiredService<IProcessRunner>(),
        deviceCaptureConfig.AndroidCaInstallScript));
builder.Services.AddSingleton<IDeviceCaInstaller>(sp =>
    new IosCaInstaller(
        sp.GetRequiredService<IProcessRunner>(),
        new IosCaInstaller.SshConfig(
            deviceCaptureConfig.IosSshHost, deviceCaptureConfig.IosSshPort, deviceCaptureConfig.IosSshKeyPath,
            deviceCaptureConfig.IosCaInstallCommand, deviceCaptureConfig.IosTrustStorePath)));
builder.Services.AddSingleton(sp => {
    var config = sp.GetRequiredService<IConfiguration>();
    string contentRoot = ContentRoot.Resolve(config["ContentRoot"]);
    string capturePath = config["CapturePath"] ?? Path.Combine(contentRoot, "captures");
    string caPath = config["CaPath"] ?? Path.Combine(capturePath, "eggincognito-ca.cer");
    return new DeviceCaptureManager(
        deviceCaptureConfig, deviceConfig, capturePath, caPath, null, contentRoot,
        sp.GetRequiredService<ILogger<DeviceCaptureManager>>(),
        sp.GetServices<IDeviceCaInstaller>(),
        sp.GetRequiredService<DataCatalog>().WireRoutes().ToHashSet(StringComparer.Ordinal),
        sp.GetService<PeriodicalsChangeNotifier>(),
        sp.GetRequiredService<IRouteCatalog>());
});
builder.Services.AddSingleton<DeviceProxyPusher>();
if (deviceCaptureConfig.Enabled && deviceConfig.Devices.Count > 0)
    builder.Services.AddHostedService(sp => sp.GetRequiredService<DeviceCaptureManager>());


builder.Services.AddHttpClient("itunes", c => c.Timeout = TimeSpan.FromSeconds(10));
builder.Services.AddHttpClient("play", c => {
    c.Timeout = TimeSpan.FromSeconds(15);
    c.DefaultRequestHeaders.UserAgent.ParseAdd(
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0 Safari/537.36");
    c.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
});
builder.Services.AddSingleton<KnownVersionRecorder>();
builder.Services.AddSingleton<IosStoreCatalog>();
builder.Services.AddSingleton<AndroidStoreCatalog>();
builder.Services.AddSingleton<IDeviceJobTracker,
    DeviceJobTracker>();

string androidDrive = builder.Configuration["DeviceUpdate:Android:DriveCommand"]
                      ?? builder.Configuration["DeviceCheck:Android:DriveCommand"]
                      ?? "am start -a android.intent.action.VIEW -d market://details?id={package}";
int androidPollSeconds = builder.Configuration.GetValue<int?>("DeviceUpdate:Android:PollSeconds")
                         ?? builder.Configuration.GetValue("DeviceCheck:Android:PollSeconds", 15);
int androidPollAttempts = builder.Configuration.GetValue<int?>("DeviceUpdate:Android:PollAttempts")
                          ?? builder.Configuration.GetValue("DeviceCheck:Android:PollAttempts", 24);
int androidUiFirstWait = builder.Configuration.GetValue("DeviceUpdate:Android:UiFirstWaitSeconds", 3);
int androidUiRetryWait = builder.Configuration.GetValue("DeviceUpdate:Android:UiRetryWaitSeconds", 2);
string? androidLookupCountry = builder.Configuration["DeviceUpdate:Android:LookupCountry"];
string? androidLookupLocale = builder.Configuration["DeviceUpdate:Android:LookupLocale"] ?? "en";
builder.Services.AddSingleton<IDeviceStoreChecker>(sp =>
    new StoreUpdateOrchestrator(
        new AndroidStoreUpdateDriver(
            sp.GetRequiredService<IProcessRunner>(),
            new AndroidStoreUpdateDriver.Options(androidDrive, androidUiFirstWait, androidUiRetryWait,
                androidLookupCountry, androidLookupLocale),
            sp.GetRequiredService<AndroidStoreCatalog>(),
            sp.GetRequiredService<KnownVersionRecorder>(),
            sp.GetRequiredService<ILogger<AndroidStoreUpdateDriver>>()),
        new StoreUpdateOrchestrator.Options(androidPollSeconds, androidPollAttempts),
        sp.GetRequiredService<KnownVersionRecorder>(),
        sp.GetRequiredService<ILoggerFactory>().CreateLogger("device.storeupdate.android")));

var iosUpdateConfig = builder.Configuration.GetSection("DeviceUpdate").GetSection("Ios");
string? iosSshHost = iosUpdateConfig["SshHost"];
string iosSshPort = iosUpdateConfig["SshPort"] ?? "2222";
string? iosSshKeyPath = iosUpdateConfig["SshKeyPath"];
string iosTriggerPath = iosUpdateConfig["TriggerPath"] ?? "/var/mobile/eggupdate.trigger";
int iosPollSeconds = iosUpdateConfig.GetValue("PollSeconds", 15);
int iosPollAttempts = iosUpdateConfig.GetValue("PollAttempts", 24);
string iosAppId = iosUpdateConfig["AppId"] ?? "993492744";
string? iosLookupCountry = iosUpdateConfig["LookupCountry"];
builder.Services.AddSingleton<IDeviceStoreChecker>(sp =>
    new StoreUpdateOrchestrator(
        new IosStoreUpdateDriver(
            sp.GetRequiredService<IProcessRunner>(),
            new IosStoreUpdateDriver.Options(
                iosSshHost, iosSshPort, iosSshKeyPath, iosTriggerPath, iosAppId, iosLookupCountry),
            sp.GetRequiredService<IosStoreCatalog>(),
            sp.GetRequiredService<KnownVersionRecorder>(),
            sp.GetRequiredService<ILogger<IosStoreUpdateDriver>>()),
        new StoreUpdateOrchestrator.Options(iosPollSeconds, iosPollAttempts),
        sp.GetRequiredService<KnownVersionRecorder>(),
        sp.GetRequiredService<ILoggerFactory>().CreateLogger("device.storeupdate.ios")));

builder.Services.AddSingleton<IDevicePlatform, IosPlatform>();
builder.Services.AddSingleton<IDevicePlatform, AndroidPlatform>();
builder.Services.AddSingleton<IDevicePlatforms, DevicePlatforms>();

if (hostedCaptureOn) {
    if (string.IsNullOrWhiteSpace(hostedCaptureOpts.AddressSecret)) {
        throw new InvalidOperationException(
            "Capture:AddressSecret must be set when hosted capture is enabled (it is the HMAC key for per-user proxy addresses).");
    }

    builder.Services.AddSingleton(sp => {
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("capture.frontdoor");
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();


        async Task<string?> addrToUser(IPAddress addr) {
            using var scope = scopeFactory.CreateScope();
            var store = scope.ServiceProvider.GetService<CaptureAddressStore>();
            if (store is null) return null;
            var userId = await store.UserForAddrAsync(addr);
            if (userId is null) return null;
            var identity = scope.ServiceProvider.GetService<IdentityApiClient>();
            if (identity is null) return null;
            var user = await identity.GetAsync(userId.Value, CancellationToken.None);
            return user?.DiscordId;
        }

        return new ProxyFrontDoor(
            hostedCaptureOpts,
            sp.GetRequiredService<CaptureSessionManager>(),
            addrToUser,
            msg => logger.LogInformation("{Message}", msg));
    });
    builder.Services.AddHostedService(sp => sp.GetRequiredService<ProxyFrontDoor>());
    builder.Services.TryAddSingleton(TimeProvider.System);
    builder.Services.AddHostedService<CaptureSweeper>();
}

var app = builder.Build();

if (dbEnabled) {
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<EggIncognitoDbContext>();
    await db.Database.MigrateAsync();
    await RouteSeeder.SeedAsync(
        db, scope.ServiceProvider.GetRequiredService<RouteCatalog>());
    await TagSeeder.SeedAsync(db);


    {
        var deviceStore = scope.ServiceProvider.GetService<IDeviceStatusStore>();
        if (deviceStore is not null) {
            var flat = deviceConfig.Devices
                .Select(d => (d.Id, d.Platform, d.Label, d.Target, d.Package)).ToList();
            await DeviceSeeder.SeedAsync(deviceStore, db, flat);
        }
    }
    app.Logger.LogInformation("Postgres DB layer active: migrated + seeded yaml routes + tags.");
} else {
    app.Logger.LogInformation("No ConnectionStrings:Postgres - running file-only (no DB overlay).");
}

app.UseForwardedHeaders();


app.Use(async (ctx, next) => {
    ctx.Request.Headers.Remove("Sec-WebSocket-Extensions");
    await next();
});

app.UseExceptionHandler();

app.Use(async (ctx, next) => {
    if (ctx.Request.Host.Host.StartsWith("protos.", StringComparison.OrdinalIgnoreCase)
        && ctx.Request.Path == "/") {
        ctx.Request.Path = "/protos";
    }

    await next();
});

app.UseStaticFiles();

app.UseRouting();
if (authEnabled) {
    app.UseAuthentication();
    app.UseMiddleware<ApiKeyResolutionMiddleware>();
    app.UseAuthorization();

    app.UseMiddleware<LoginCallbackMiddleware>();
}

app.UseAntiforgery();
app.UseRateLimiter();
app.UseEggIdentityRequestMetrics();

app.MapControllers();
if (!string.IsNullOrWhiteSpace(eventSecret)) {
    var ingest = app.Services.GetRequiredService<NewVersionIngestService>();
    app.MapPost("/events/new-version", NewVersionHandler.Build(eventSecret, evt => ingest.HandleAsync(evt)))
        .RequireRateLimiting("write");
}

if (!string.IsNullOrWhiteSpace(botToken) && dbEnabled) {
    await using var adminConn = await NpgsqlDataSource.Create(pgConn!).OpenConnectionAsync();
    await Migrator.MigrateAsync(adminConn, Path.Combine(AppContext.BaseDirectory, "Migrations"));
}

if (!string.IsNullOrWhiteSpace(botToken) && dbEnabled) {
    var deployDataSource = NpgsqlDataSource.Create(pgConn!);
    var configStore = new ChannelConfigStore(deployDataSource);
    var botCfg = app.Services.GetRequiredService<BotConfig>();
    var hosted = app.Services.GetRequiredService<EggIncognitoBotHostedService>();
    app.Lifetime.ApplicationStarted.Register(() => _ = Task.Run(async () => {
        var client = hosted.Bot?.Client;
        if (client is null || !ulong.TryParse(botCfg.GuildId, out ulong guildId)) return;
        var notifier = new DeployNotifier(configStore, client, guildId, botCfg.Name);
        var tracker = new DeployVersionTracker(new DeployStateStore(deployDataSource), notifier);
        try {
            await tracker.CheckAndNotifyAsync(
                botCfg.Name, Environment.GetEnvironmentVariable("GIT_SHA") ?? "", botCfg.Build.Version,
                CancellationToken.None);
        } catch (Exception ex) {
            app.Logger.LogWarning(ex, "deploy notify failed");
        }
    }));
}

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();
app.MapGet("/health", () => Results.Ok());
app.MapGet("/api/app/mode", (IAppMode m, AuthState auth, ICurrentUser user) =>
    Results.Ok(new {
        mode = m.Mode.ToString(),
        canCapture = m.CanCapture,
        canWrite = m.CanWrite,
        hostedCapture = m.HostedCaptureEnabled,
        authEnabled = auth.Enabled,
        user = user.IsAuthenticated
            ? new {
                user.DiscordId,
                user.Username,
                user.Avatar,
                role = UserRoles.ToName(user.Role),
                supporter = user.IsSupporter
            }
            : null
    }));

if (authEnabled) {
    app.MapPost("/api/account/refresh-benefits",
        (HttpContext http, ICurrentUser user, SupporterStatus checker) => {
            if (!user.IsAuthenticated || string.IsNullOrEmpty(user.DiscordId))
                return Results.Unauthorized();
            checker.Invalidate(user.DiscordId);
            return Results.Redirect("/support");
        }).RequireRateLimiting("read");
}

app.Lifetime.ApplicationStopping.Register(fileLogProvider.Dispose);

bool signing = app.Services.GetRequiredService<ITransportPipeline>().CanSign;
app.Logger.LogInformation("WebRootPath = {WebRoot}", app.Environment.WebRootPath);
app.Logger.LogInformation("Request signing: {State} (EGG_INC_API_SALT {SaltState})",
    signing ? "ready" : "DISABLED", signing ? "set" : "not set");
app.Logger.LogInformation("Log file: {LogFile}", fileLogProvider.FilePath ?? "(file logging disabled)");

if (captureMode) {
    app.Lifetime.ApplicationStarted.Register(() => {
        var sess = app.Services.GetRequiredService<CaptureSession>();
        _ = sess.StartAsync(CancellationToken.None);
    });
}

bool servesOverKestrel = app.Services.GetRequiredService<IServer>()
    .GetType().Name == "KestrelServer";
if (servesOverKestrel &&
    app.Environment.IsDevelopment() &&
    !app.Configuration.GetValue("NoBrowser", false)) {
    app.Lifetime.ApplicationStarted.Register(() => _ = Task.Run(async () => {
        string addr = app.Services.GetRequiredService<IServer>()
                          .Features.Get<IServerAddressesFeature>()
                          ?.Addresses.FirstOrDefault(a => a.StartsWith("http://", StringComparison.Ordinal))
                      ?? "http://localhost:5032";


        if (captureMode) {
            await Task.Delay(TimeSpan.FromSeconds(1.5));
            var hub = app.Services.GetRequiredService<CaptureSession>().Hub;
            if (hub.HasSubscribers) {
                app.Logger.LogInformation("Dashboard already open (reconnected) - not opening a new tab.");
                return;
            }
        }

        string url = addr.TrimEnd('/') + (captureMode ? "/capture" : "/inspector");
        try {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        } catch (Exception ex) {
            app.Logger.LogWarning(ex, "Could not auto-open browser at {Url}", url);
        }
    }));
}

await app.RunAsync();
return 0;
