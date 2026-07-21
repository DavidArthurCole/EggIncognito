using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using EggIncognito.Logging;
using EggIncognito.Services;
using EggIncognito.Services.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("EggIncognito.Tests")]

if (args.Length >= 3 && args[0] is "__extract-proto" or "__extract-ios-proto")
    return EggIncognito.Build.IosProtoExtractor.Run(args[1], args[2]);

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

if (Environment.GetEnvironmentVariable("EGGINCOGNITO_TEST_DBFREE") == "1"
    && string.IsNullOrEmpty(builder.Configuration["TestDbOptIn"]))
{
    builder.Configuration["ConnectionStrings:Postgres"] = "";
}
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

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();


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
   
    c.DefaultRequestHeaders.Add("User-Agent",
        "Dalvik/2.1.0 (Linux; U; Android 9; SM-G960U1 Build/PPR1.180610.011)");
    c.DefaultRequestHeaders.Add("Accept-Encoding", "gzip");
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    AutomaticDecompression = System.Net.DecompressionMethods.GZip,
});

builder.Services.AddScoped<EggIncognito.Services.ShipShellDownloader>();

builder.Services.AddSingleton<EggIncognito.Services.MeshAssetCache>();
builder.Services.AddSingleton<EggIncognito.Services.Assets.IconAssetCache>();

builder.Services.AddScoped<EggIncognito.Services.Devices.IDeviceAssetReader, EggIncognito.Services.Devices.AndroidDeviceAssetReader>();
builder.Services.AddScoped<EggIncognito.Services.Devices.IDeviceAssetReader, EggIncognito.Services.Devices.IosDeviceAssetReader>();
builder.Services.AddScoped<EggIncognito.Services.Devices.DeviceAssetService>();

builder.Services.AddScoped<EggIncognito.Core.Services.Assets.IGameAssetTier, EggIncognito.Services.Assets.MeshDbTier>();
builder.Services.AddScoped<EggIncognito.Core.Services.Assets.IGameAssetTier, EggIncognito.Services.Assets.MeshDiskTier>();
builder.Services.AddScoped<EggIncognito.Core.Services.Assets.IGameAssetTier, EggIncognito.Services.Assets.ConfigDiskTier>();
builder.Services.AddScoped<EggIncognito.Core.Services.Assets.IGameAssetTier, EggIncognito.Services.Assets.IconDbTier>();
builder.Services.AddScoped<EggIncognito.Core.Services.Assets.IGameAssetTier, EggIncognito.Services.Assets.IconDiskTier>();
builder.Services.AddScoped<EggIncognito.Core.Services.Assets.IGameAssetOrigin, EggIncognito.Services.Assets.MeshDeviceOrigin>();
builder.Services.AddScoped<EggIncognito.Core.Services.Assets.IGameAssetOrigin, EggIncognito.Services.Assets.IconDeviceOrigin>();
builder.Services.AddScoped<EggIncognito.Core.Services.Assets.IGameAssetOrigin, EggIncognito.Services.Assets.IconCdnOrigin>();
builder.Services.AddScoped<EggIncognito.Core.Services.Assets.GameAssetProvider>();

builder.Services.AddScoped<EggIncognito.Services.DeviceMeshProvider>();

builder.Services.AddScoped<EggIncognito.Services.GameBinaryProvider>();

builder.Services.AddSingleton<EggIncognito.Services.GameConfigStore>();
builder.Services.AddSingleton<EggIncognito.Services.Feed.PeriodicalsChangeNotifier>();
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
builder.Services.AddSingleton<EggIncognito.GameData.IGameDataProvider>(_ => EggIncognito.GameData.GameDataProvider.CreateDefault());
builder.Services.AddSingleton<IDocRegistry, DocRegistry>();
builder.Services.AddSingleton<ITransportPipeline, TransportPipeline>();

var pgConn = builder.Configuration.GetConnectionString("Postgres");
var dbEnabled = !string.IsNullOrWhiteSpace(pgConn);
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

builder.Services.AddSingleton<RouteCatalog>();
builder.Services.AddSingleton<AuxbrainSurface>();
builder.Services.AddSingleton<IRouteCatalog>(sp =>
    new MergedRouteCatalog(
        sp.GetRequiredService<RouteCatalog>(),
        dbEnabled ? sp.GetRequiredService<IDbRouteProvider>() : null));

if (dbEnabled)
{
    builder.Services.AddDbContextPool<EggIncognito.Data.Services.EggIncognitoDbContext>(o => o.UseNpgsql(pgConn));
   
   
    builder.Services.AddDataProtection()
        .SetApplicationName("EggIncognito")
        .PersistKeysToDbContext<EggIncognito.Data.Services.EggIncognitoDbContext>();
   
    builder.Services.AddScoped<EggIncognito.Data.Services.DbEndpointSource>();
    builder.Services.AddScoped(sp =>
        new DbEndpointSourceMarker(sp.GetRequiredService<EggIncognito.Data.Services.DbEndpointSource>()));
   
   
    builder.Services.AddScoped<EggIncognito.Data.Services.DbRouteProvider>();
    builder.Services.AddSingleton<IDbRouteProvider>(sp =>
        new EggIncognito.Data.Services.ScopedDbRouteProvider(sp.GetRequiredService<IServiceScopeFactory>()));
}

var identityApiUrl = builder.Configuration["Identity:ApiUrl"];
var identityApiSecret = builder.Configuration["Identity:ApiSecret"];

var identityWidgetUrl = builder.Configuration["Identity:WidgetUrl"];
var identityApiEnabled = !string.IsNullOrWhiteSpace(identityApiUrl) && !string.IsNullOrWhiteSpace(identityApiSecret);
if (identityApiEnabled)
{
    builder.Services.AddHttpClient<SyncKit.Identity.Client.IdentityApiClient>(c =>
    {
        c.BaseAddress = new Uri(identityApiUrl!);
        c.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", identityApiSecret);
    });
}
builder.AddSyncKitAuthIfConfigured(identityApiEnabled);
var authState = new AuthState(identityApiEnabled, identityWidgetUrl);
var authEnabled = authState.Enabled;
builder.Services.AddSingleton(authState);
builder.Services.AddScoped<EggIncognito.Services.LoginSignIn>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<EggIncognito.Services.Metrics.ApiMetrics>();
builder.Services.AddSingleton<EggIncognito.Services.Metrics.ApiAuditLog>();
builder.Services.TryAddScoped<ICurrentUser, CurrentUser>();

builder.Services.AddHttpClient("discord-api", c => c.Timeout = TimeSpan.FromSeconds(8));
builder.Services.AddSingleton<SupporterStatus>();
builder.Services.AddSingleton<ISupporterStatus>(sp => sp.GetRequiredService<SupporterStatus>());
if (!string.IsNullOrWhiteSpace(builder.Configuration["Discord:BotToken"]))
    builder.Services.AddSingleton<ICaptureCaNotifier, DiscordCaptureCaNotifier>();
else
    builder.Services.AddSingleton<ICaptureCaNotifier, NoopCaptureCaNotifier>();

var botToken = builder.Configuration["Discord:BotToken"];
if (!string.IsNullOrWhiteSpace(botToken))
{
    const string repoUrl = "https://github.com/DavidArthurCole/EggIncognito";
    var buildInfo = EggIncognito.Services.BuildInfo.FromAssembly(repoUrl);
    var startedAt = DateTimeOffset.UtcNow;

    builder.Services.AddSingleton(new EggIncognito.Services.RepoUrl(repoUrl));
    builder.Services.AddSingleton<EggIncognito.Bot.IStatusProvider, EggIncognito.Services.StatusSnapshotFactory>();

   
   
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
           
            SharedRoleId = builder.Configuration["SHARED_ROLE_ID"] ?? builder.Configuration["Discord:SharedRoleId"] ?? "",
           
            DeployAgentUrl = builder.Configuration["DEPLOY_AGENT_URL"] ?? builder.Configuration["Discord:DeployAgentUrl"] ?? "",
            DeployAgentSecret = builder.Configuration["DEPLOY_AGENT_SECRET"] ?? builder.Configuration["Discord:DeployAgentSecret"] ?? "",
            PostgresConnectionString = dbEnabled ? pgConn! : "",
            DashboardChannelId = builder.Configuration["Discord:DashboardChannelId"] ?? "",
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
   
   
    builder.Services.AddScoped(sp =>
        sp.GetRequiredService<EggIncognito.Bot.EggIncognitoBotHostedService>().Bot?.ConfigService!);
}

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
       
       
        var expectedProtoSha = EggIncognito.Core.ProtoHash.Current(syncContentRoot);
        var notifier = sp.GetRequiredService<EggIncognito.Bot.ISyncNotifier>();
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("sync.ingest");

       
       
        async Task Registry(SyncKit.Contract.NewVersionEvent evt, CancellationToken ct)
        {
            using var scope = sp.CreateScope();
            var store = scope.ServiceProvider.GetService<EggIncognito.Data.Services.ProtoRegistryStore>();
            if (store is null) return;
            string? protoText = string.IsNullOrEmpty(evt.ProtoTextB64) ? null
                : System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(evt.ProtoTextB64));
           
            var appVersion = string.IsNullOrEmpty(evt.AppVersion) ? evt.Version : evt.AppVersion;
            var build = string.IsNullOrEmpty(evt.Build) ? evt.Version : evt.Build;
            if (string.IsNullOrEmpty(build) || string.IsNullOrEmpty(appVersion)) return;
           
            var platform = evt.Platform ?? "android";
            var (row, created, protoChanged) = await store.UpsertAsync(
                platform, appVersion, build, evt.ClientVersion, evt.Package, evt.ProtoSha, evt.ApkRef,
                DateTimeOffset.TryParse(evt.DetectedAt, out var dt) ? dt : DateTimeOffset.UtcNow,
                detectedBy: null, protoText, source: "farm", ct: ct);

           
            var dispatcher = scope.ServiceProvider.GetService<EggIncognito.Services.Feed.FeedDispatcher>();
            if (dispatcher is not null)
            {
                var cfg = scope.ServiceProvider.GetService<IConfiguration>();
                var pageUrl = EggIncognito.Services.Feed.FeedDispatcher.BuildPageUrl(
                    cfg?["Feed:PageBaseUrl"], platform, build);
                await dispatcher.DispatchAsync(new EggIncognito.Services.Feed.ProtoBuildEvent(
                    row.Id, platform, appVersion, build, evt.ClientVersion,
                    evt.ProtoSha, created, protoChanged, pageUrl), ct);
            }
        }

       
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

       
       
        Task Regen(SyncKit.Contract.NewVersionEvent evt, CancellationToken ct)
        {
            EndpointExtractor.ForRepo(syncContentRoot, eid: null, "EI0000000000000000", overwrite: true);
            logger.LogInformation("sync: staged area ready for {Version}; apk-driven regen not yet wired", evt.Version);
            return Task.CompletedTask;
        }

       
       
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
var hostedCaptureOpts = EggIncognito.Capture.HostedCaptureOptions.Bind(builder.Configuration);
builder.Services.AddSingleton(hostedCaptureOpts);
builder.Services.AddSingleton(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
   
   
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


    builder.Services.AddScoped<EggIncognito.Data.Services.IProtoBackfillStore>(
        sp => sp.GetRequiredService<EggIncognito.Data.Services.ProtoRegistryStore>());
    builder.Services.AddHttpClient("github");
    builder.Services.AddScoped<EggIncognito.Services.Backfill.IGitHubClient, EggIncognito.Services.Backfill.GitHubClient>();
    builder.Services.AddScoped<EggIncognito.Services.Backfill.ElgranjeroImporter>();
    builder.Services.AddScoped<EggIncognito.Services.Backfill.PlayStoreImporter>();
    builder.Services.AddScoped<EggIncognito.Services.Backfill.AppStoreImporter>();

   
   
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

   
   
    var pollerOptions = EggIncognito.Services.Backfill.VersionPollerOptions.Bind(builder.Configuration);
    builder.Services.AddSingleton(pollerOptions);
    if (pollerOptions.Enabled)
        builder.Services.AddHostedService<EggIncognito.Services.Backfill.VersionPollerService>();
}


var deviceConfig = EggIncognito.Services.Devices.DeviceConfig.Bind(builder.Configuration);
builder.Services.AddSingleton(deviceConfig);
builder.Services.AddSingleton<EggIncognito.Core.Services.Devices.IProcessRunner, EggIncognito.Core.Services.Devices.ProcessRunner>();
builder.Services.TryAddSingleton(TimeProvider.System);
if (deviceConfig.Enabled && deviceConfig.Devices.Count > 0)
    builder.Services.AddHostedService<EggIncognito.Services.Devices.DeviceProbeService>();


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


var androidDrive = builder.Configuration["DeviceCheck:Android:DriveCommand"]
    ?? "am start -a android.intent.action.VIEW -d market://details?id={package}";
var androidPollSeconds = builder.Configuration.GetValue("DeviceCheck:Android:PollSeconds", 15);
var androidPollAttempts = builder.Configuration.GetValue("DeviceCheck:Android:PollAttempts", 24);
builder.Services.AddSingleton<EggIncognito.Core.Services.Devices.IDeviceStoreChecker>(sp =>
    new EggIncognito.Services.Devices.AndroidPlayStoreChecker(
        sp.GetRequiredService<EggIncognito.Core.Services.Devices.IProcessRunner>(),
        new EggIncognito.Services.Devices.AndroidPlayStoreChecker.Options(androidDrive, androidPollSeconds, androidPollAttempts),
        sp.GetRequiredService<ILogger<EggIncognito.Services.Devices.AndroidPlayStoreChecker>>()));
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
            if (store is null) return null;
            var userId = await store.UserForAddrAsync(addr);
            if (userId is null) return null;
            var identity = scope.ServiceProvider.GetService<SyncKit.Identity.Client.IdentityApiClient>();
            if (identity is null) return null;
            var user = await identity.GetAsync(userId.Value, CancellationToken.None);
            return user?.DiscordId;
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

if (dbEnabled)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<EggIncognito.Data.Services.EggIncognitoDbContext>();
    await db.Database.MigrateAsync();
    await EggIncognito.Data.Services.RouteSeeder.SeedAsync(
        db, scope.ServiceProvider.GetRequiredService<RouteCatalog>());
    await EggIncognito.Data.Services.TagSeeder.SeedAsync(db);
   
   
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

app.UseForwardedHeaders();


app.Use(async (ctx, next) =>
{
    ctx.Request.Headers.Remove("Sec-WebSocket-Extensions");
    await next();
});

app.UseExceptionHandler();

app.Use(async (ctx, next) =>
{
    if (ctx.Request.Host.Host.StartsWith("protos.", StringComparison.OrdinalIgnoreCase)
        && ctx.Request.Path == "/")
    {
        ctx.Request.Path = "/protos";
    }
    await next();
});

app.UseStaticFiles();

app.UseRouting();
if (authEnabled)
{
    app.UseAuthentication();
    app.UseAuthorization();
   
    app.UseMiddleware<EggIncognito.Services.LoginCallbackMiddleware>();
}
app.UseAntiforgery();
app.UseRateLimiter();

{
    var metrics = app.Services.GetRequiredService<EggIncognito.Services.Metrics.ApiMetrics>();
    var audit = app.Services.GetRequiredService<EggIncognito.Services.Metrics.ApiAuditLog>();
    var appMode = app.Services.GetRequiredService<IAppMode>();
    app.Use(async (ctx, next) =>
    {
        var isApi = ctx.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase);
        await next();
        if (!isApi) return;
        var limited = ctx.Response.StatusCode == StatusCodes.Status429TooManyRequests;
        metrics.Record(limited);

        var user = ctx.RequestServices.GetService<ICurrentUser>();
        var bucket = user is null
            ? EggIncognito.Services.Metrics.RequestBucket.External
            : EggIncognito.Services.Metrics.RequestBucketClassifier.Classify(ctx, user);
        var ip = EggIncognito.Services.Metrics.RequestBucketClassifier.Ip(ctx, appMode.Mode == AppMode.Hosted);
        audit.Record(ctx.Request.Method, ctx.Request.Path.Value ?? "/", ctx.Response.StatusCode,
            bucket, ip, user?.DiscordId);
    });
}

app.MapControllers();
if (!string.IsNullOrWhiteSpace(eventSecret))
{
    var ingest = app.Services.GetRequiredService<EggIncognito.Services.NewVersionIngestService>();
    app.MapPost("/events/new-version", SyncKit.Bot.NewVersionHandler.Build(eventSecret, evt => ingest.HandleAsync(evt)))
        .RequireRateLimiting("write");
}

if (!string.IsNullOrWhiteSpace(botToken) && dbEnabled)
{
    await using var adminConn = await Npgsql.NpgsqlDataSource.Create(pgConn!).OpenConnectionAsync();
    await SyncKit.Db.Migrator.MigrateAsync(adminConn, Path.Combine(AppContext.BaseDirectory, "Migrations"));
}

var deployNotifySecret = builder.Configuration["DEPLOY_NOTIFY_SECRET"] ?? "";
if (!string.IsNullOrWhiteSpace(deployNotifySecret) && !string.IsNullOrWhiteSpace(botToken) && dbEnabled)
{
    var deployDataSource = Npgsql.NpgsqlDataSource.Create(pgConn!);
    var configStore = new SyncKit.Bot.ChannelConfigStore(deployDataSource);
    var botCfg = app.Services.GetRequiredService<SyncKit.Bot.BotConfig>();
    var hosted = app.Services.GetRequiredService<EggIncognito.Bot.EggIncognitoBotHostedService>();
    app.MapPost("/internal/deploy-notify", SyncKit.Bot.DeployNotifyHandler.Build(deployNotifySecret, async res =>
    {
        var client = hosted.Bot?.Client;
        if (client is null || !ulong.TryParse(botCfg.GuildId, out var guildId)) return;
        var notifier = new SyncKit.Bot.DeployNotifier(configStore, client, guildId, botCfg.Name);
        await notifier.NotifyAsync(res, CancellationToken.None);
    })).RequireRateLimiting("write");
}
app.MapRazorComponents<EggIncognito.Components.App>()
   .AddInteractiveServerRenderMode();
app.MapGet("/health", () => Results.Ok());
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
app.Lifetime.ApplicationStopping.Register(fileLogProvider.Dispose);

var signing = app.Services.GetRequiredService<ITransportPipeline>().CanSign;
app.Logger.LogInformation("WebRootPath = {WebRoot}", app.Environment.WebRootPath);
app.Logger.LogInformation("Request signing: {State} (EGG_INC_API_SALT {SaltState})",
    signing ? "ready" : "DISABLED", signing ? "set" : "not set");
app.Logger.LogInformation("Log file: {LogFile}", fileLogProvider.FilePath ?? "(file logging disabled)");

if (captureMode)
{
    app.Lifetime.ApplicationStarted.Register(() =>
    {
        var sess = app.Services.GetRequiredService<EggIncognito.Capture.CaptureSession>();
        _ = sess.StartAsync(CancellationToken.None);
    });
}

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
