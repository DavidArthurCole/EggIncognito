using System.Security.Cryptography.X509Certificates;
using EggIncognito.Services;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("EggIncognito.Tests")]

var builder = WebApplication.CreateBuilder(args);

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
builder.Services.AddSingleton<IEndpointCatalog, EndpointCatalog>();
builder.Services.AddSingleton<IProtoReflection, ProtoReflection>();
builder.Services.AddSingleton<ITransportPipeline, TransportPipeline>();
builder.Services.AddSingleton<IFixtureStore>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var logger = sp.GetRequiredService<ILogger<FixtureStore>>();
    var path = config["FixturesPath"]
        ?? Path.Combine(AppContext.BaseDirectory, "Fixtures");
    return new FixtureStore(path, logger);
});

var app = builder.Build();

// Static files MUST short-circuit before routing: SimulationController has a
// catch-all [HttpOptions("/{**slug}")] that otherwise makes every GET report
// 405 (Allow: OPTIONS). UseDefaultFiles maps /inspector/ -> /inspector/index.html.
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseRouting();
app.MapControllers();
app.MapGet("/health", () => Results.Ok());
app.MapGet("/inspector", () => Results.Redirect("/inspector/"));

app.Logger.LogInformation("WebRootPath = {WebRoot}", app.Environment.WebRootPath);
await app.RunAsync();
