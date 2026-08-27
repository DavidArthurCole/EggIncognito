using System.Diagnostics;
using System.Runtime.CompilerServices;
using EggIncognito.Build;
using EggIncognito.Capture;
using EggIncognito.Services;
using EggIncognito.Startup;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

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
var fileLogProvider = builder.AddAppHosting();
var boot = BootFlags.From(builder);

builder.AddWebServices();
builder.AddAssetServices();
builder.AddCoreServices();
builder.AddWorkbenchServices();
builder.AddEndpointAndRouteSources(boot);
builder.AddDatabaseServices(boot);
builder.AddIdentityServices(boot);
builder.AddBotServices(boot);
builder.AddSyncIngest(boot);
builder.AddDatabaseStores(boot);
builder.AddCaptureServices(boot);
builder.AddDeviceServices(boot);

var app = builder.Build();

await app.InitializeAsync(boot);
app.UseAppPipeline(boot);
await app.RunBotMigrationsAsync(boot);
app.MapAppEndpoints(boot);

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

        string url = addr.TrimEnd('/') + (captureMode ? "/protos#api/capture" : "/protos#api");
        try {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        } catch (Exception ex) {
            app.Logger.LogWarning(ex, "Could not auto-open browser at {Url}", url);
        }
    }));
}

await app.RunAsync();
return 0;
