using System.Security.Cryptography.X509Certificates;
using EggIncognito.Cli;
using EggIncognito.Logging;
using EggIncognito.Services;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("EggIncognito.Tests")]

// CLI subcommand dispatch: these run and exit without booting the web host. Anything else
// (no args, or `serve` + flags like --capture) falls through to the web app below.
if (args.Length > 0 && args[0] is "emit-types" or "check-endpoints" or "export-collection"
    or "seed" or "from-har" or "decode")
{
    var rest = args[1..];
    return args[0] switch
    {
        "emit-types" => TypeEmitter.Run(RepoPaths.FindRoot()),
        "check-endpoints" => CheckEndpointsCommand.Run(rest),
        "export-collection" => ExportCollectionCommand.Run(rest),
        "seed" => await SeedCommand.RunSeedAsync(rest),
        "from-har" => SeedCommand.RunFromHar(rest),
        "decode" => SeedCommand.RunDecode(rest),
        _ => 1,
    };
}

var builder = WebApplication.CreateBuilder(args);

// --- Logging: console (default) + in-memory ring buffer (Inspector Logs panel) +
// one file per process start. The standard ILoggerProvider model keeps these swappable;
// a remote sink (Papertrail/Seq/OTel) can be added alongside without touching call sites.
var logStore = new InMemoryLogStore(capacity: 2000);
builder.Services.AddSingleton<IInMemoryLogStore>(logStore);
builder.Logging.AddProvider(new InMemoryLoggerProvider(logStore));

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
builder.Services.AddSingleton<IBehaviorService, BehaviorService>();
builder.Services.AddSingleton<IRouteCatalog, RouteCatalog>();
builder.Services.AddSingleton<IProtoReflection, ProtoReflection>();
builder.Services.AddSingleton<ITransportPipeline, TransportPipeline>();
builder.Services.AddSingleton<IEndpointStore>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var logger = sp.GetRequiredService<ILogger<EndpointStore>>();
    var path = config["EndpointsPath"]
        ?? Path.Combine(AppContext.BaseDirectory, "Endpoints");
    return new EndpointStore(path, logger);
});
builder.Services.AddSingleton(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var repoRoot = EggIncognito.Cli.RepoPaths.FindRoot();
    var capturePath = config["CapturePath"] ?? Path.Combine(repoRoot, "captures");
    var caPath = config["CaPath"] ?? Path.Combine(capturePath, "eggincognito-ca.cer");
    var opts = new EggIncognito.Capture.CaptureSessionOptions(
        Port: int.TryParse(config["CapturePort"], out var cp) ? cp : 8080,
        Eid: config["EGG_INC_EID"] ?? Environment.GetEnvironmentVariable("EGG_INC_EID"),
        Label: config["CaptureLabel"],
        Overwrite: config.GetValue("CaptureOverwrite", false),
        Verbose: config.GetValue("CaptureVerbose", false),
        CapturePath: capturePath,
        CaPath: caPath);
    return new EggIncognito.Capture.CaptureSession(repoRoot, opts);
});

var app = builder.Build();

// App-wide structured error handling. Turns ApiException into {error, resolution, status}
// and any unhandled exception into a 500 that points at the logs.
app.UseExceptionHandler();

// Static files MUST short-circuit before routing: SimulationController has a
// catch-all [HttpOptions("/{**slug}")] that otherwise makes every GET report
// 405 (Allow: OPTIONS). UseDefaultFiles maps /inspector/ -> /inspector/index.html.
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseRouting();
app.MapControllers();
app.MapGet("/health", () => Results.Ok());
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

// Auto-open the inspector in the browser on startup. launchSettings' launchBrowser is
// only honored by dotnet watch / VS / VS Code - plain `dotnet run` ignores it - so open
// it ourselves. Development only, and skippable with NoBrowser=true (Docker sets this).
if (app.Environment.IsDevelopment() &&
    !app.Configuration.GetValue("NoBrowser", false))
{
    app.Lifetime.ApplicationStarted.Register(() =>
    {
        var addr = app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>()
            .Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()
            ?.Addresses.FirstOrDefault(a => a.StartsWith("http://"))
            ?? "http://localhost:5032";
        var url = addr.TrimEnd('/') + "/inspector/";
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            app.Logger.LogWarning(ex, "Could not auto-open browser at {Url}", url);
        }
    });
}

// Opt-in capture auto-start: `--capture` (or Capture=true config) brings the proxy up once the
// host is listening. Off by default; otherwise toggled at runtime via POST /api/capture/start.
if (app.Configuration.GetValue("capture", false) || args.Contains("--capture"))
{
    app.Lifetime.ApplicationStarted.Register(() =>
    {
        var sess = app.Services.GetRequiredService<EggIncognito.Capture.CaptureSession>();
        _ = sess.StartAsync(CancellationToken.None);
    });
}

await app.RunAsync();
return 0;
