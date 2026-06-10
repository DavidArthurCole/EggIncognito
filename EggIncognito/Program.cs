using System.Security.Cryptography.X509Certificates;
using EggIncognito.Logging;
using EggIncognito.Services;
using EggIncognito.Services.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("EggIncognito.Tests")]

// Build-time hook (NOT a user command): the EmitDashboardTypes MSBuild target invokes
// `dotnet run -- __emit-types <outPath>` to regenerate wwwroot/capture/types.d.ts from the C#
// records. It runs and exits without booting the web host. The former user-facing CLI subcommands
// (check-endpoints / export-collection / seed / from-har / decode) are gone - their behavior now
// lives behind the web UI (Tools + Import tabs).
if (args.Length >= 2 && args[0] == "__emit-types")
    return EggIncognito.Build.TypeEmitter.Run(args[1]);

// Capture launch: the `--capture` flag starts the proxy once the host is up and opens the Capture tab
// instead of the Inspector. `--eid` / `--label` configure the session (mapped onto the config keys
// CaptureSession reads).
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

// Logging: console (default) + one file per process start. The standard ILoggerProvider model keeps
// these swappable; a remote sink (Papertrail/Seq/OTel) can be added alongside without touching call sites.
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
    if (!File.Exists(certFile) || !File.Exists(keyFile)) return;

    var httpPort = int.TryParse(context.Configuration["HttpPort"], out var hp) ? hp : 8080;
    var httpsPort = int.TryParse(context.Configuration["HttpsPort"], out var sp) ? sp : 8443;
    opts.ListenAnyIP(httpPort);
    opts.ListenAnyIP(httpsPort, o => o.UseHttps(X509Certificate2.CreateFromPemFile(certFile, keyFile)));
});

builder.Services.AddControllers();
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
builder.Services.AddSingleton<IAppMode, AppModeService>();
builder.Services.AddSingleton<IBehaviorService, BehaviorService>();
builder.Services.AddSingleton<IProtoReflection, ProtoReflection>();
builder.Services.AddSingleton<ITransportPipeline, TransportPipeline>();

// Endpoints + routes: a file source always; a Postgres overlay + DB-only routes when a connection
// string is configured (always in Hosted; optional in Local). With no connection string the app is
// the file-only Phase 0 app, byte-for-byte.
var pgConn = builder.Configuration.GetConnectionString("Postgres");
var dbEnabled = !string.IsNullOrWhiteSpace(pgConn);

builder.Services.AddSingleton<IEndpointStore>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var logger = sp.GetRequiredService<ILogger<EndpointStore>>();
    var path = config["EndpointsPath"] ?? Path.Combine(AppContext.BaseDirectory, "Endpoints");
    var fileSource = new FileEndpointSource(path);
    var scopeFactory = dbEnabled ? sp.GetRequiredService<IServiceScopeFactory>() : null;
    return new EndpointStore(fileSource, scopeFactory, logger);
});

builder.Services.AddSingleton<RouteCatalog>();           // the yaml catalog (concrete)
builder.Services.AddSingleton<IRouteCatalog>(sp =>
    new MergedRouteCatalog(
        sp.GetRequiredService<RouteCatalog>(),
        dbEnabled ? sp.GetRequiredService<IDbRouteProvider>() : null));

if (dbEnabled)
{
    builder.Services.AddDbContextPool<EggIncognito.Data.Services.EggIncognitoDbContext>(o => o.UseNpgsql(pgConn));
    // Scoped DB endpoint source + a marker so the singleton EndpointStore can resolve it from a scope.
    builder.Services.AddScoped<EggIncognito.Data.Services.DbEndpointSource>();
    builder.Services.AddScoped(sp =>
        new DbEndpointSourceMarker(sp.GetRequiredService<EggIncognito.Data.Services.DbEndpointSource>()));
    // Scoped DB route provider + a singleton adapter that opens a scope per call (MergedRouteCatalog
    // is a singleton and cannot capture the scoped provider directly).
    builder.Services.AddScoped<EggIncognito.Data.Services.DbRouteProvider>();
    builder.Services.AddSingleton<IDbRouteProvider>(sp =>
        new EggIncognito.Data.Services.ScopedDbRouteProvider(sp.GetRequiredService<IServiceScopeFactory>()));
}

// Discord auth wires only when a DB + Discord creds are present. AuthState records the result so the
// always-present AuthController + mode endpoint can branch. CurrentUser is registered always (reports
// anonymous when no auth middleware ran), so those consumers construct in both modes.
var authEnabled = builder.AddDiscordAuthIfConfigured(dbEnabled);
builder.Services.AddSingleton(new AuthState(authEnabled));
builder.Services.AddHttpContextAccessor();
builder.Services.TryAddScoped<ICurrentUser, CurrentUser>();
// The admin allowlist (Discord:AdminIds) bootstraps the first admin; registered always (harmless when
// empty) so UserUpsert can resolve it during login.
builder.Services.AddSingleton(
    EggIncognito.Data.Services.AdminAllowlist.FromConfig(builder.Configuration["Discord:AdminIds"]));

// Discord bot (gateway presence + slash commands). Opt-in: only when Discord:BotToken is set. With no
// token nothing is registered and the app is unchanged. Reuses Discord:ClientId as the application id;
// Discord:GuildId (optional) also registers commands to one guild for instant testing.
var botToken = builder.Configuration["Discord:BotToken"];
if (!string.IsNullOrWhiteSpace(botToken))
{
    builder.Services.AddSingleton(new EggIncognito.Bot.BotOptions(
        Token: botToken,
        ApplicationId: builder.Configuration["Discord:ClientId"] ?? "",
        GuildId: builder.Configuration["Discord:GuildId"],
        RepoUrl: "https://github.com/DavidArthurCole/EggIncognito"));
    builder.Services.AddSingleton<EggIncognito.Bot.IStatusProvider, EggIncognito.Services.StatusSnapshotFactory>();
    builder.Services.AddHostedService<EggIncognito.Bot.DiscordBotHostedService>();
}

builder.Services.AddSingleton(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    // Content root = the directory that directly holds RouteMap/ + Endpoints/ (the project dir in
    // dev, the exe dir when published). CaptureSession + EndpointExtractor both resolve their files
    // under it - no more repo-root walk + "EggIncognito" prefix.
    var contentRoot = ContentRoot.Resolve(config["ContentRoot"]);
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
});

var app = builder.Build();

// Apply migrations + mirror yaml routes into stored_routes when a DB is configured. Fail fast on a
// broken DB in any mode (a hosted deploy must not silently serve files when its DB is misconfigured).
if (dbEnabled)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<EggIncognito.Data.Services.EggIncognitoDbContext>();
    await db.Database.MigrateAsync();
    await EggIncognito.Data.Services.RouteSeeder.SeedAsync(
        db, scope.ServiceProvider.GetRequiredService<RouteCatalog>());
    await EggIncognito.Data.Services.TagSeeder.SeedAsync(db);
    app.Logger.LogInformation("Postgres DB layer active: migrated + seeded yaml routes + tags.");
}
else
{
    app.Logger.LogInformation("No ConnectionStrings:Postgres - running file-only (no DB overlay).");
}

// App-wide structured error handling. Turns ApiException into {error, resolution, status}
// and any unhandled exception into a 500 that points at the logs.
app.UseExceptionHandler();

// Static files MUST short-circuit before routing: SimulationController has a
// catch-all [HttpOptions("/{**slug}")] that otherwise makes every GET report
// 405 (Allow: OPTIONS). UseDefaultFiles maps /inspector/ -> /inspector/index.html.
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseRouting();
if (authEnabled)
{
    app.UseAuthentication();
    app.UseAuthorization();
}
app.UseRateLimiter();
app.MapControllers();
app.MapGet("/health", () => Results.Ok());
// The UI fetches this on load to gate features: Capture/Import nav links + save/update buttons are
// hidden when the matching capability is off (hosted deploy sets AppMode=Hosted). It also carries the
// auth state + current user so the SPA renders login state in one fetch (authEnabled=false hides it).
app.MapGet("/api/app/mode", (IAppMode m, AuthState auth, ICurrentUser user) =>
    Results.Ok(new
    {
        mode = m.Mode.ToString(),
        canCapture = m.CanCapture,
        canWrite = m.CanWrite,
        authEnabled = auth.Enabled,
        user = user.IsAuthenticated
            ? new { user.DiscordId, user.Username, user.Avatar, role = EggIncognito.Data.Models.UserRoles.ToName(user.Role) }
            : null,
    }));
// `/` serves wwwroot/index.html (the landing page) via UseDefaultFiles; no redirect.
app.MapGet("/inspector", () => Results.Redirect("/inspector/"));
app.MapGet("/capture", () => Results.Redirect("/capture/"));

// Flush and close the per-startup log file cleanly on shutdown.
app.Lifetime.ApplicationStopping.Register(fileLogProvider.Dispose);

var signing = app.Services.GetRequiredService<ITransportPipeline>().CanSign;
app.Logger.LogInformation("WebRootPath = {WebRoot}", app.Environment.WebRootPath);
app.Logger.LogInformation("Request signing: {State} (EGG_INC_API_SALT {SaltState})",
    signing ? "ready" : "DISABLED", signing ? "set" : "not set");
app.Logger.LogInformation("Log file: {LogFile}", fileLogProvider.FilePath ?? "(file logging disabled)");

// In capture mode, start the proxy once the host is listening (otherwise it is toggled at runtime
// via POST /api/capture/start).
if (captureMode)
{
    app.Lifetime.ApplicationStarted.Register(() =>
    {
        var sess = app.Services.GetRequiredService<EggIncognito.Capture.CaptureSession>();
        _ = sess.StartAsync(CancellationToken.None);
    });
}

// Auto-open the browser on startup - the Capture tab in capture mode, otherwise the Inspector.
// launchSettings' launchBrowser is only honored by dotnet watch / VS / VS Code - plain `dotnet run`
// ignores it - so open it ourselves. Development only, skippable with NoBrowser=true (Docker sets
// this). Also skipped under the test host: WebApplicationFactory boots the real Program in
// Development but serves via TestServer (not Kestrel), so we must never spawn a real browser there.
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
            // within ~1s of this process binding. Wait briefly, and if a client has already attached,
            // skip opening - the user's existing page is now driven by this server. (Capture only;
            // the Inspector has no live connection to detect.)
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

            var url = addr.TrimEnd('/') + (captureMode ? "/capture/" : "/inspector/");
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
