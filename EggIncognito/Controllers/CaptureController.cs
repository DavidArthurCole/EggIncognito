using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using EggIncognito.Capture;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.Services;

namespace EggIncognito.Controllers;

// Backend for the capture dashboard + the runtime start/stop control. Routes under /api/capture.
// Local resolves the single anonymous session (old singleton behavior, gated by CanCapture). Hosted
// with HostedCaptureEnabled resolves the caller's own per-user session via the manager; everything
// else stays a consistent 403.
[ApiController]
[Route("api/capture")]
public sealed class CaptureController(
    CaptureSessionManager manager,
    IAppMode appMode,
    ICurrentUser currentUser,
    ISupporterStatus supporters,
    HostedCaptureOptions hostedOptions,
    IServiceProvider services) : ControllerBase
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private CaptureCredentialStore? Credentials =>
        services.GetService(typeof(CaptureCredentialStore)) as CaptureCredentialStore;

    // Resolve the caller's session. Local: the anonymous local session when CanCapture, else the old
    // 403. Hosted+enabled: own session only; 401 anonymous, 404 when none exists yet.
    private (CaptureSession? Session, IActionResult? Error) Resolve()
    {
        if (appMode.CanCapture)
            return (manager.GetOrCreate(CaptureSessionManager.LocalKey), null);
        if (!appMode.HostedCaptureEnabled)
            return (null, StatusCode(403, new { error = "capture is disabled in hosted mode" }));
        if (!currentUser.IsAuthenticated || string.IsNullOrEmpty(currentUser.DiscordId))
            return (null, StatusCode(401, new { error = "log in to use hosted capture" }));
        var session = manager.Get(currentUser.DiscordId);
        return session is null
            ? (null, NotFound(new { error = "no capture session; start one on /capture first" }))
            : (session, null);
    }

    // Hosted-capture write surface gate (token mint, CA download): supporter claim required.
    private IActionResult? RequireHostedSupporter()
    {
        if (!appMode.HostedCaptureEnabled)
            return StatusCode(403, new { error = "hosted capture is not enabled" });
        if (!currentUser.IsAuthenticated || string.IsNullOrEmpty(currentUser.DiscordId))
            return StatusCode(401, new { error = "log in to use hosted capture" });
        if (!currentUser.IsSupporter)
            return StatusCode(403, new { error = "supporter_required" });
        return null;
    }

    [HttpGet("stream")]
    public async Task Stream(CancellationToken ct)
    {
        var (session, error) = Resolve();
        if (session is null)
        {
            // Raw-response action (no IActionResult), so write the guard's status by hand.
            var (status, payload) = error is ObjectResult o
                ? (o.StatusCode ?? 403, o.Value)
                : (403, (object?)new { error = "capture unavailable" });
            Response.StatusCode = status;
            await Response.WriteAsJsonAsync(payload, ct);
            return;
        }

        Response.Headers.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";
        await Response.WriteAsync(":ok\n\n", ct);
        await Response.Body.FlushAsync(ct);

        var (reader, subscription) = session.Hub.Subscribe();
        using (subscription)
        {
            foreach (var f in session.Hub.Snapshot()) await WriteEvent("flow", f, ct);
            await WriteEvent("stats", session.Hub.StatsSnapshot(), ct);
            try
            {
                await foreach (var env in reader.ReadAllAsync(ct))
                {
                    object? payload = env.Kind switch
                    {
                        "flow" => env.Flow,
                        "stats" => env.Stats,
                        "notice" => env.Event,
                        _ => null,
                    };
                    if (payload is not null) await WriteEvent(env.Kind, payload, ct);
                }
            }
            catch (OperationCanceledException) { /* client disconnected */ }
        }
    }

    private async Task WriteEvent(string eventName, object payload, CancellationToken ct)
    {
        var data = JsonSerializer.Serialize(payload, Json);
        await Response.WriteAsync($"event: {eventName}\ndata: {data}\n\n", ct);
        await Response.Body.FlushAsync(ct);
    }

    [HttpGet("flows")]
    public IActionResult Flows()
    {
        var (session, error) = Resolve();
        return session is null ? error! : Ok(session.Hub.Snapshot());
    }

    [HttpGet("sensitive-keys")]
    public IActionResult SensitiveKeys() => Ok(new
    {
        keys = Redactor.SensitiveFieldNames.Concat(["eiUserId", "userId"]).Distinct().ToArray(),
    });

    [HttpGet("stats")]
    public IActionResult Stats()
    {
        var (session, error) = Resolve();
        return session is null ? error! : Ok(session.Hub.StatsSnapshot());
    }

    [HttpGet("status")]
    public IActionResult Status()
    {
        var (session, error) = Resolve();
        return session is null ? error! : Ok(session.Status);
    }

    [HttpPost("start")]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> Start(CancellationToken ct)
    {
        if (appMode.CanCapture)
            return Ok(await manager.GetOrCreate(CaptureSessionManager.LocalKey).StartAsync(ct));
        if (!appMode.HostedCaptureEnabled)
            return StatusCode(403, new { error = "capture is disabled in hosted mode" });
        if (!currentUser.IsAuthenticated || string.IsNullOrEmpty(currentUser.DiscordId))
            return StatusCode(401, new { error = "log in to use hosted capture" });
        // Live role re-check; the cookie claim alone must not spin up server resources.
        if (!await supporters.CheckAsync(currentUser.DiscordId, ct))
            return StatusCode(403, new { error = "supporter_required" });

        CaptureSession session;
        try { session = manager.GetOrCreate(currentUser.DiscordId); }
        catch (CaptureCapacityException)
        {
            return StatusCode(503, new { error = "capture capacity reached; try again later" });
        }

        var store = Credentials;
        await RestoreCaAsync(session, store, currentUser.DiscordId, ct);
        var result = await session.StartAsync(ct);
        if (result.FreshCa && store is not null)
            await PersistFreshCaAsync(session, store, currentUser.DiscordId, result.RootThumbprint, ct);
        // DM the setup (CA profile + freshly-minted token) on every start, not just the first session:
        // the token rotates per session and the user needs the install profile each time they set up a
        // device. Best-effort; never fails the start.
        await DeliverSetupAsync(session, currentUser.DiscordId, store, ct);
        return Ok(result);
    }

    // Mint a fresh proxy token and DM the install profile + connection details so the user never hunts
    // for a download or copies a long token by hand. Runs on every session start; the token plaintext
    // only exists at mint time, so minting + DMing happen together. Best-effort: a closed-DM or bot
    // failure sets a session notice and never fails the start.
    private async Task DeliverSetupAsync(
        CaptureSession session, string discordId, CaptureCredentialStore? store, CancellationToken ct)
    {
        var notifier = services.GetService(typeof(ICaptureCaNotifier)) as ICaptureCaNotifier;
        if (notifier is null || store is null) { FlagDmFailed(session); return; }

        byte[] cer;
        try { cer = await System.IO.File.ReadAllBytesAsync(session.CaPath, ct); }
        catch { cer = []; }
        if (cer.Length == 0) { FlagDmFailed(session); return; }

        var token = CaptureCredentialStore.MintToken();
        await store.SetTokenAsync(discordId, CaptureCredentialStore.Hash(token), ct);

        var dm = new CaptureSetupDm(
            discordId, cer, hostedOptions.PublicHost, hostedOptions.FrontDoorPort, discordId, token);
        if (await notifier.SendSetupAsync(dm, ct)) return;
        FlagDmFailed(session);
    }

    private static void FlagDmFailed(CaptureSession session)
    {
        session.CaDmFailed = true;
        session.Hub.PostNotice(new CaptureEvent(
            "caDmFailed", "Could not DM your setup; use the card below.", DateTime.Now.ToString("HH:mm:ss")));
    }

    // Unobtanium reuses the root CA at {caDir}/.ca/root.pfx; restoring the stored pfx there before
    // start means the user's device keeps trusting the same cert across sessions and hosts.
    private static async Task RestoreCaAsync(
        CaptureSession session, CaptureCredentialStore? store, string discordId, CancellationToken ct)
    {
        if (store is null) return;
        var pfxPath = SessionPfxPath(session);
        if (System.IO.File.Exists(pfxPath)) return;
        var ca = await store.GetCaAsync(discordId, ct);
        if (ca is null || ca.Pfx.Length == 0) return;
        Directory.CreateDirectory(Path.GetDirectoryName(pfxPath)!);
        await System.IO.File.WriteAllBytesAsync(pfxPath, ca.Pfx, ct);
    }

    // A fresh mint means no stored CA matched; persist the pfx Unobtanium just wrote so the user
    // installs the cert once, ever.
    private static async Task PersistFreshCaAsync(
        CaptureSession session, CaptureCredentialStore store, string discordId,
        string? thumbprint, CancellationToken ct)
    {
        var pfxPath = SessionPfxPath(session);
        if (!System.IO.File.Exists(pfxPath)) return;
        var pfx = await System.IO.File.ReadAllBytesAsync(pfxPath, ct);
        await store.SaveCaAsync(discordId, pfx, thumbprint ?? "", ct);
    }

    private static string SessionPfxPath(CaptureSession session) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(session.CaPath))!, ".ca", "root.pfx");

    [HttpPost("stop")]
    public async Task<IActionResult> Stop()
    {
        var (session, error) = Resolve();
        if (session is null) return error!;
        await session.StopAsync();
        return Ok(new { running = false });
    }

    [HttpPost("pause")]
    public IActionResult Pause()
    {
        var (session, error) = Resolve();
        if (session is null) return error!;
        session.Hub.Paused = true;
        return Ok(new { paused = true });
    }

    [HttpPost("resume")]
    public IActionResult Resume()
    {
        var (session, error) = Resolve();
        if (session is null) return error!;
        session.Hub.Paused = false;
        return Ok(new { paused = false });
    }

    [HttpPost("clear")]
    public IActionResult Clear()
    {
        var (session, error) = Resolve();
        if (session is null) return error!;
        session.Hub.Clear();
        return Ok(new { cleared = true });
    }

    public sealed record SaveEndpointRequest(long Id);

    [HttpPost("save-endpoint")]
    public async Task<IActionResult> SaveEndpoint([FromBody] SaveEndpointRequest body,
        [FromServices] IRouteCatalog routes)
    {
        var (session, error) = Resolve();
        if (session is null) return error!;
        var flow = session.Hub.Find(body.Id);
        if (flow is null) return NotFound(new { error = $"flow {body.Id} not in buffer" });

        if (appMode.CanCapture)
        {
            // Local: write the endpoint fixture to disk via the extractor, as before.
            var path = session.SaveEndpoint(flow.Path, flow.Method, flow.Status, flow.RequestDataB64, flow.ResponseB64);
            if (path is null) return StatusCode(409, new { error = "capture not running or flow could not be decoded" });
            session.Hub.MarkSaved(body.Id); // so a refresh does not re-prompt to save the same capture
            return Ok(new { saved = path });
        }

        // Hosted: supporters + contributors save to the shared DB store; file writes stay off
        // (CanWrite remains false in Hosted).
        if (!currentUser.IsSupporter && !currentUser.IsAtLeast(UserRole.Contributor))
            return StatusCode(403, new { error = "supporter or contributor role required to save endpoints" });
        var db = services.GetService(typeof(EggIncognitoDbContext)) as EggIncognitoDbContext;
        if (db is null) return StatusCode(503, new { error = "no database configured" });
        if (routes.Get(flow.Path) is null) return BadRequest(new { error = $"unknown route {flow.Path}" });
        var decoded = session.Decode(flow.Path, flow.ResponseB64);
        if (decoded.Json is null || decoded.Type is null)
            return StatusCode(409, new { error = "flow could not be decoded" });

        var existing = await db.StoredEndpoints
            .FirstOrDefaultAsync(e => e.Path == flow.Path && e.Eid == null);
        if (existing is null)
        {
            db.StoredEndpoints.Add(new StoredEndpoint
            {
                Path = flow.Path, Eid = null,
                ResponseJson = decoded.Json, ResponseType = decoded.Type,
                OwnerUserId = currentUser.DiscordId,
            });
        }
        else
        {
            existing.ResponseJson = decoded.Json;
            existing.ResponseType = decoded.Type;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }
        await db.SaveChangesAsync();
        session.Hub.MarkSaved(body.Id);
        return Ok(new { saved = flow.Path, store = "db" });
    }

    [HttpGet("har")]
    public IActionResult Har()
    {
        var (session, error) = Resolve();
        if (session is null) return error!;
        var bytes = Encoding.UTF8.GetBytes(session.CurrentHar());
        return File(bytes, "application/json", "capture-session.har");
    }

    [HttpGet("decode")]
    public IActionResult Decode([FromQuery] string path, [FromQuery] string responseB64)
    {
        var (session, error) = Resolve();
        if (session is null) return error!;
        var r = session.Decode(path, responseB64);
        return Ok(new { responseJson = r.Json, responseType = r.Type, known = r.Known });
    }

    // Mint or rotate the caller's proxy token. The plaintext is returned exactly once; only the
    // SHA-256 hash is stored.
    [HttpPost("proxy-token")]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> MintProxyToken(CancellationToken ct)
    {
        if (RequireHostedSupporter() is { } no) return no;
        var store = Credentials;
        if (store is null) return StatusCode(503, new { error = "no database configured" });
        var token = CaptureCredentialStore.MintToken();
        await store.SetTokenAsync(currentUser.DiscordId!, CaptureCredentialStore.Hash(token), ct);
        return Ok(new { username = currentUser.DiscordId, token });
    }

    // A per-SSID .mobileconfig that applies the Manual proxy automatically when the device joins the
    // named Wi-Fi network. Mints a fresh token (same rotation as the card/DM) and bakes it into the
    // profile so it stays valid. The Wi-Fi password is never carried.
    [HttpGet("proxy-profile")]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> DownloadProxyProfile([FromQuery] string ssid, CancellationToken ct)
    {
        if (RequireHostedSupporter() is { } no) return no;
        if (string.IsNullOrWhiteSpace(ssid)) return StatusCode(400, new { error = "ssid required" });
        ssid = ssid.Trim();
        if (ssid.Length > 64) return StatusCode(400, new { error = "ssid too long" });
        var store = Credentials;
        if (store is null) return StatusCode(503, new { error = "no database configured" });
        var token = CaptureCredentialStore.MintToken();
        await store.SetTokenAsync(currentUser.DiscordId!, CaptureCredentialStore.Hash(token), ct);
        var bytes = MobileConfig.BuildProxyProfile(
            ssid, hostedOptions.PublicHost, hostedOptions.FrontDoorPort, currentUser.DiscordId!, token);
        return File(bytes, "application/x-apple-aspen-config", "eggincognito-proxy.mobileconfig");
    }

    // The caller's capture CA as a device-installable .cer. Prefers the live session's exported
    // cert; falls back to the public half of the stored pfx.
    [HttpGet("ca.cer")]
    public async Task<IActionResult> DownloadCa(CancellationToken ct)
    {
        if (RequireHostedSupporter() is { } no) return no;
        var session = manager.Get(currentUser.DiscordId!);
        if (session is not null && System.IO.File.Exists(session.CaPath))
        {
            var cer = await System.IO.File.ReadAllBytesAsync(session.CaPath, ct);
            return File(cer, "application/x-x509-ca-cert", "eggincognito-ca.cer");
        }
        var store = Credentials;
        if (store is null) return StatusCode(503, new { error = "no database configured" });
        var ca = await store.GetCaAsync(currentUser.DiscordId!, ct);
        if (ca is null || ca.Pfx.Length == 0)
            return NotFound(new { error = "no CA yet; start a capture session first" });
        try
        {
            using var cert = X509CertificateLoader.LoadPkcs12(ca.Pfx, password: null);
            return File(cert.Export(X509ContentType.Cert), "application/x-x509-ca-cert", "eggincognito-ca.cer");
        }
        catch (CryptographicException)
        {
            return StatusCode(500, new { error = "stored CA could not be read" });
        }
    }
}
