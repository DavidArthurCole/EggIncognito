using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using EggIncognito.Services;
using EggIncognito.Tooling.Dashboard;

namespace EggIncognito.Tooling.Capture;

// `capture` subcommand. Runs the selective-decrypt proxy, and for every captured auxbrain flow:
//   (a) appends a HAR entry (the durable, re-runnable hand-off artifact),
//   (b) feeds the flow to EndpointExtractor in-process (fixtures + routes.yaml self-repair),
//   (c) publishes a decoded-for-display copy to the live dashboard (CaptureHub -> SSE).
// A small Kestrel host serves the dashboard SPA + /api/capture, mirroring the mock server's
// architecture so the tools can merge into one app later.
// On Ctrl-C: stop the web host + proxy, flush the HAR, save the yaml editor, print the report.
public static class CaptureCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        var opts = CaptureOptions.Parse(args);
        var port = opts.Port;
        var dashboardPort = opts.DashboardPort;
        var eid = opts.Eid;
        var verbose = opts.Verbose;
        var noDashboard = opts.NoDashboard;
        var noOpen = opts.NoOpen;

        var repoRoot = FindRepoRoot();
        var capturesDir = Path.Combine(repoRoot, "captures");
        Directory.CreateDirectory(capturesDir);
        var caPath = Path.Combine(capturesDir, "eggincognito-ca.cer");
        var harPath = UniquePath(Path.Combine(capturesDir, opts.HarFileName()));

        const string eidPlaceholder = "EI0000000000000000";
        var extractor = EndpointExtractor.ForRepo(repoRoot, eid, eidPlaceholder, opts.Overwrite);
        var har = new HarWriter();
        var hub = new CaptureHub();
        var decoder = new FlowDecoder(repoRoot);

        await using var proxy = new UnobtaniumCaptureProxy(verbose);
        if (verbose) proxy.Trace += line => Console.WriteLine($"  [trace] {line}");

        // Heavy per-flow work (proto auto-detect decode, fixture write, HAR append) must NOT run on
        // the proxy's response thread - doing so stalls the tunnel and makes both the game and the
        // dashboard lag. Hand each captured flow to a background queue; a single consumer processes
        // them in order while the proxy returns immediately.
        var flowQueue = System.Threading.Channels.Channel.CreateUnbounded<CapturedFlow>(
            new System.Threading.Channels.UnboundedChannelOptions { SingleReader = true });
        proxy.FlowCaptured += flow => flowQueue.Writer.TryWrite(flow);

        // All per-flow decode/write/diff logic lives in FlowProcessor (testable; sets the extractor
        // Quiet so its console chatter does not leak into the capture output). The consumer just
        // pumps the queue and publishes each result to the hub.
        var flowProcessor = new FlowProcessor(extractor, decoder, har, repoRoot);
        var processor = Task.Run(async () =>
        {
            await foreach (var flow in flowQueue.Reader.ReadAllAsync())
            {
                try { hub.Publish(flowProcessor.Process(flow), Now()); }
                catch (Exception ex) { Console.Error.WriteLine($"  flow-process error: {ex.Message}"); }
            }
        });

        // Connection + health signals -> hub (drives the device toast, stats, and cert pill).
        proxy.ClientConnected += (count, ip) => hub.RecordConnection(count, ip, Now());
        proxy.ClientDisconnected += (count, ip) => hub.RecordDisconnection(count, Now());
        proxy.AuxbrainConnect += () => hub.RecordAuxbrainConnect();
        proxy.DecryptError += msg => hub.RecordDecryptError(msg, Now());

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, ev) => { ev.Cancel = true; cts.Cancel(); };

        // Boot the dashboard web host (unless disabled). If the port is already serving (a previous
        // capture run is still up), do NOT start a second host or open another browser tab - the
        // user just re-ran; reuse what is open. We still warn so it is not silent.
        WebApplication? web = null;
        var dashboardAlreadyUp = !noDashboard && await IsDashboardUp(dashboardPort);
        if (!noDashboard && !dashboardAlreadyUp)
        {
            web = BuildDashboard(dashboardPort, hub, har, extractor, decoder);
            await web.StartAsync(cts.Token);
        }

        await proxy.StartAsync(port, caPath, cts.Token);
        Console.WriteLine($"Capture proxy listening on port {port}.");

        // Cert state, printed every run so it is never ambiguous whether you must reinstall.
        if (proxy.FreshCa)
        {
            Console.WriteLine();
            Console.WriteLine("***************************************************************************");
            Console.WriteLine("*  NEW root CA was just created. You MUST install it on the device ONCE:  *");
            Console.WriteLine($"*    {caPath}");
            Console.WriteLine($"*    subject: CN=EggIncognito Capture Root  thumbprint: {proxy.RootThumbprint}");
            Console.WriteLine("*  After this, the CA is reused every run - no more reinstalling.        *");
            Console.WriteLine("***************************************************************************");
            Console.WriteLine();
        }
        else
        {
            Console.WriteLine($"Reusing persisted root CA (thumbprint {proxy.RootThumbprint}) - no device reinstall needed.");
        }
        var dashUrl = $"http://localhost:{dashboardPort}/";
        if (web is not null)
        {
            Console.WriteLine($"Live dashboard: {dashUrl}");
            // Open a browser tab only the FIRST time the dashboard is used (tracked by a marker
            // file). The tab survives server restarts - it reconnects over SSE - so re-running the
            // capture must NOT spawn another tab. `--open` forces a new tab regardless.
            var marker = Path.Combine(capturesDir, $".dashboard-open-{dashboardPort}");
            if (!noOpen && (opts.ForceOpen || !File.Exists(marker)))
            {
                TryOpenBrowser(dashUrl);
                try { File.WriteAllText(marker, dashUrl); } catch { /* non-fatal */ }
            }
            else if (!noOpen)
            {
                Console.WriteLine("Dashboard tab already opened earlier - reusing it (pass --open to force a new tab).");
            }
        }
        else if (dashboardAlreadyUp)
        {
            // A previous capture run is still serving the dashboard on this port. Reuse it.
            Console.WriteLine($"Dashboard already running at {dashUrl} - reusing it (no new tab).");
        }
        Console.WriteLine("Point the device HTTP proxy at this machine, then exercise the game.");
        Console.WriteLine("Only *.auxbrain.com / *-dot-auxbrainhome.appspot.com is decrypted; all other traffic passes through.");
        Console.WriteLine("Press Ctrl-C to stop and write the capture.");

        try { await Task.Delay(Timeout.Infinite, cts.Token); }
        catch (TaskCanceledException) { /* Ctrl-C */ }

        Console.WriteLine();
        Console.WriteLine("Stopping...");
        await proxy.StopAsync();

        // Drain any queued flows before writing the HAR/fixtures so nothing in flight is lost.
        flowQueue.Writer.TryComplete();
        await processor;

        if (web is not null) await web.StopAsync();

        if (har.Count > 0)
        {
            har.Save(harPath);
            Console.WriteLine($"Wrote {har.Count} flow(s) -> {harPath}");
        }
        else
        {
            Console.WriteLine("No auxbrain flows captured.");
        }

        extractor.Save();
        var c = extractor.Counts;
        Console.WriteLine($"new={c.Wrote}  upd={c.Upd}  diff={c.Diff}  same={c.Same}  loss={c.Loss}  err={c.Err}");
        extractor.PrintSelfRepairReport();
        return 0;
    }

    // Minimal Kestrel host: static SPA (wwwroot/capture) + the /api/capture controller. Mirrors
    // the mock server's middleware order (static files before routing).
    private static WebApplication BuildDashboard(
        int port, CaptureHub hub, HarWriter har, EndpointExtractor extractor, FlowDecoder decoder)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls($"http://localhost:{port}");
        builder.Logging.ClearProviders(); // keep the console clean for capture output
        builder.Services.AddControllers();
        builder.Services.AddSingleton(hub);
        builder.Services.AddSingleton(har);
        builder.Services.AddSingleton(extractor);
        builder.Services.AddSingleton(decoder);

        var app = builder.Build();
        app.UseDefaultFiles();   // serve /capture/index.html at /capture/
        // No-cache the dashboard SPA so an updated app.js/index.html/styles.css is always served
        // fresh - the browser must not keep a stale cached build. (Verified: app.js comes back with
        // Cache-Control: no-cache.) A hard-refresh once after an update fully clears module cache.
        app.UseStaticFiles(new StaticFileOptions
        {
            OnPrepareResponse = ctx =>
            {
                ctx.Context.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
                ctx.Context.Response.Headers.Pragma = "no-cache";
                ctx.Context.Response.Headers.Expires = "0";
            }
        });
        app.UseRouting();
        app.MapControllers();
        app.MapGet("/", () => Results.Redirect("/capture/"));
        return app;
    }

    private static string Now() => DateTime.Now.ToString("HH:mm:ss");

    // Never overwrite an existing capture: if the target HAR exists, append _2, _3, ... before the
    // extension until a free name is found.
    internal static string UniquePath(string path)
    {
        if (!File.Exists(path)) return path;
        var dir = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (int i = 2; ; i++)
        {
            var candidate = Path.Combine(dir, $"{name}_{i}{ext}");
            if (!File.Exists(candidate)) return candidate;
        }
    }

    private static void TryOpenBrowser(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* headless / no browser - the URL is printed above */ }
    }

    // True if a dashboard from a previous run is already serving on this port (so we should not
    // start a second host or open another browser tab). Probes the capture API quickly.
    private static async Task<bool> IsDashboardUp(int port)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMilliseconds(400) };
            var resp = await http.GetAsync($"http://localhost:{port}/api/capture/flows");
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    private static string FindRepoRoot()
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
}
