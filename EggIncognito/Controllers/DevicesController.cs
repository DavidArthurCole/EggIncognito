using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.Core.Services.Devices;
using EggIncognito.Services;
using EggIncognito.Services.Devices;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EggIncognito.Controllers;

// Device status (public read) + admin refresh. Reads project the latest probe per device; refresh runs an
// immediate probe tagged admin:<id> through the same DeviceProbeRunner the background poller uses. DB-gated:
// reads return [] without Postgres, writes return 503. Admin gate mirrors AdminController.RequireAdmin.
[ApiController]
[Route("api/devices")]
[EnableRateLimiting("read")]
public sealed class DevicesController(
    ICurrentUser currentUser, IServiceProvider services,
    IServiceScopeFactory scopeFactory, IDeviceJobTracker jobs) : ControllerBase
{
    private IDeviceStatusStore? Store => services.GetService(typeof(IDeviceStatusStore)) as IDeviceStatusStore;
    private EggIncognitoDbContext? Db => services.GetService(typeof(EggIncognitoDbContext)) as EggIncognitoDbContext;

    private IActionResult? RequireAdmin() =>
        currentUser.IsAtLeast(UserRole.Admin) ? null : StatusCode(403, new { error = "admin role required" });

    [HttpGet("status")]
    [EnableRateLimiting("fetch")] // public + polled every 8s by DeviceStatusPanel; not the class "read" cap
    public async Task<IActionResult> Status()
    {
        var store = Store;
        if (store is null) return Ok(Array.Empty<object>());
        var latest = await store.LatestPerDeviceAsync();
        var devices = (await store.EnabledDevicesAsync()).ToDictionary(d => d.Id);
        var updates = (await store.LatestUpdatePerDeviceAsync()).ToDictionary(u => u.DeviceId);

        // store-latest per distinct platform, so the Update button shows only when the store is ahead of
        // what is installed. One lookup per platform, reused across that platform's devices.
        var db = Db;
        var storeLatest = new Dictionary<string, string?>();
        // Live registry latest per platform (non-deleted), used to re-classify at read time against the
        // current registry rather than the probe-time snapshot.
        var regLatestApp = new Dictionary<string, string?>();
        var regLatestBuild = new Dictionary<string, string?>();
        if (db is not null)
            foreach (var plat in devices.Values.Select(d => d.Platform).Distinct())
            {
                storeLatest[plat] = await StoreAheadCheck.StoreLatestAsync(db, plat, HttpContext.RequestAborted);
                // "Represented" = a live row or a merged alias (DeletedAt+CanonicalId set); only a true
                // soft-delete (DeletedAt set, CanonicalId null) drops a version out of "represented".
                var extracted = await db.ProtoVersions.AsNoTracking()
                    .Where(v => v.Platform == plat && (v.DeletedAt == null || v.CanonicalId != null))
                    .Select(v => new { v.Build, v.AppVersion })
                    .ToListAsync(HttpContext.RequestAborted);
                regLatestApp[plat] = extracted.Select(e => e.AppVersion)
                    .OrderByDescending(v => v, Comparer<string>.Create(DeviceProbeRunner.SemverCompare)).FirstOrDefault();
                regLatestBuild[plat] = plat == "android"
                    ? extracted.Select(e => e.Build).Where(b => long.TryParse(b, out _)).OrderByDescending(long.Parse).FirstOrDefault()
                    : null;
            }

        var rows = latest.Where(p => devices.ContainsKey(p.DeviceId)).Select(p =>
        {
            var d = devices[p.DeviceId];
            updates.TryGetValue(d.Id, out var up);
            var sl = storeLatest.GetValueOrDefault(d.Platform);
            // Reclassify against the live registry; falls back to the stored Result for unreachable/error rows.
            var liveResult = (p.Reachable && !string.IsNullOrEmpty(p.InstalledAppVersion))
                ? DeviceProbeRunner.Classify(
                    new DeviceProbeResult(true, p.InstalledAppVersion, p.InstalledBuild, null),
                    d.Platform, regLatestBuild.GetValueOrDefault(d.Platform), regLatestApp.GetValueOrDefault(d.Platform))
                : p.Result;
            return new
            {
                id = d.Id, platform = d.Platform, label = d.Label,
                reachable = p.Reachable,
                installedAppVersion = p.InstalledAppVersion,
                installedBuild = p.InstalledBuild,
                latestAvailable = p.LatestAvailable,
                storeLatest = sl,
                storeAhead = StoreAheadCheck.IsAhead(sl, p.InstalledAppVersion),
                result = liveResult,
                note = p.Note,
                probedAt = p.ProbedAt,
                lastUpdate = up is null ? null : new
                {
                    status = up.Status, from = up.FromVersion, to = up.ToVersion,
                    note = up.Note, by = up.TriggeredBy, at = up.AttemptedAt,
                },
            };
        });
        return Ok(rows);
    }

    [HttpGet("{id}/history")]
    public async Task<IActionResult> History(string id, [FromQuery] int n = 20)
    {
        var store = Store;
        if (store is null) return Ok(Array.Empty<object>());
        var rows = await store.HistoryAsync(id, Math.Clamp(n, 1, 100));
        return Ok(rows.Select(p => new
        {
            probedAt = p.ProbedAt, reachable = p.Reachable,
            installedAppVersion = p.InstalledAppVersion, installedBuild = p.InstalledBuild,
            result = p.Result, triggeredBy = p.TriggeredBy, note = p.Note,
        }));
    }

    [HttpPost("{id}/refresh")]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> Refresh(string id)
    {
        if (RequireAdmin() is { } no) return no;
        var store = Store;
        var db = Db;
        if (store is null || db is null) return StatusCode(503, new { error = "no database configured" });

        var device = await store.GetAsync(id);
        if (device is null) return NotFound(new { error = "unknown device" });

        var runner = (IProcessRunner)services.GetRequiredService(typeof(IProcessRunner));
        var time = (TimeProvider)services.GetRequiredService(typeof(TimeProvider));
        var logger = (ILogger<DevicesController>)services.GetRequiredService(typeof(ILogger<DevicesController>));

        var row = await DeviceProbeRunner.ProbeOneAsync(
            device, $"admin:{currentUser.DiscordId}", runner, store, db, logger, time, HttpContext.RequestAborted);

        return Ok(new
        {
            id = device.Id, platform = device.Platform, label = device.Label,
            reachable = row.Reachable, installedAppVersion = row.InstalledAppVersion,
            installedBuild = row.InstalledBuild, latestAvailable = row.LatestAvailable,
            result = row.Result, note = row.Note, probedAt = row.ProbedAt,
        });
    }

    // Fan out a probe to every enabled device (the Sources "Device farm" refresh). Admin + DB gated.
    // Best-effort per device; returns how many were probed.
    [HttpPost("refresh-all")]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> RefreshAll()
    {
        if (RequireAdmin() is { } no) return no;
        var store = Store;
        var db = Db;
        if (store is null || db is null) return StatusCode(503, new { error = "no database configured" });

        var runner = (IProcessRunner)services.GetRequiredService(typeof(IProcessRunner));
        var time = (TimeProvider)services.GetRequiredService(typeof(TimeProvider));
        var logger = (ILogger<DevicesController>)services.GetRequiredService(typeof(ILogger<DevicesController>));

        var devices = await store.EnabledDevicesAsync();
        var n = 0;
        foreach (var d in devices)
        {
            try
            {
                await DeviceProbeRunner.ProbeOneAsync(
                    d, $"admin-all:{currentUser.DiscordId}", runner, store, db, logger, time, HttpContext.RequestAborted);
                n++;
            }
            catch (Exception ex) { logger.LogWarning(ex, "refresh-all: {Id} threw", d.Id); }
        }
        return Ok(new { probed = n });
    }

    // Tell the plugged-in device to ask its own store for an Egg Inc update and install it if there is one.
    // Fire-and-forget: the store poll runs ~6 minutes, far past the reverse-proxy timeout, so this validates,
    // launches a background task, and returns 202 immediately; the UI polls GET check-status for progress.
    [HttpPost("{id}/check-update")]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> CheckUpdate(string id)
    {
        if (RequireAdmin() is { } no) return no;
        var store = Store;
        var db = Db;
        if (store is null || db is null) return StatusCode(503, new { error = "no database configured" });

        var logger = (ILogger<DevicesController>)services.GetRequiredService(typeof(ILogger<DevicesController>));
        var who = currentUser.DiscordId ?? "?";

        var device = await store.GetAsync(id);
        if (device is null) return NotFound(new { error = "unknown device" });

        var checker = services.GetServices(typeof(IDeviceStoreChecker)).Cast<IDeviceStoreChecker>()
            .FirstOrDefault(c => string.Equals(c.Platform, device.Platform, StringComparison.OrdinalIgnoreCase));
        if (checker is null)
            return StatusCode(501, new { error = $"no store checker for platform {device.Platform}" });

        // Overlap guard: refuse a second concurrent check for the same device.
        if (!jobs.TryStart(id, "checking store..."))
            return StatusCode(409, new { error = "check already running" });

        logger.LogInformation("device check-update: {Id} start (by {Who})", id, who);
        var target = new DeviceStoreTarget(device.Id, device.Platform, device.Target, device.Package);

        // Run detached with its own DI scope and CancellationToken.None: the request scope disposes and
        // HttpContext.RequestAborted fires as soon as the 202 response completes.
        _ = Task.Run(() => RunCheckUpdateAsync(id, target, checker, who));

        return Accepted(new { id = device.Id, action = "running" });
    }

    // Background body of check-update. Owns its DI scope and lifetime; every exit funnels through the
    // tracker (Finish/Fail) so the UI's check-status poll always reaches a terminal row.
    private async Task RunCheckUpdateAsync(string id, DeviceStoreTarget target, IDeviceStoreChecker checker, string who)
    {
        using var scope = scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var logger = sp.GetRequiredService<ILogger<DevicesController>>();
        try
        {
            var store = sp.GetService<IDeviceStatusStore>();
            var db = sp.GetService<EggIncognitoDbContext>();
            var runner = sp.GetRequiredService<IProcessRunner>();
            if (store is null || db is null)
            {
                jobs.Fail(id, "no database configured");
                return;
            }

            // Pre-check: only drive the device store when the store is actually ahead of what is installed,
            // so an already-current device skips the full ~6 min poll window.
            jobs.Progress(id, "reading installed version…");
            IDeviceProbe preProbe = string.Equals(target.Platform, "ios", StringComparison.OrdinalIgnoreCase)
                ? new IosDeviceProbe(runner, target.Target, target.Package)
                : new AdbDeviceProbe(runner, target.Target, target.Package);
            var probe = await preProbe.ProbeAsync(CancellationToken.None);
            var storeLatest = await StoreAheadCheck.StoreLatestAsync(db, target.Platform, CancellationToken.None);
            if (probe.Reachable && !StoreAheadCheck.IsAhead(storeLatest, probe.InstalledAppVersion))
            {
                var note = storeLatest is null
                    ? $"installed {probe.InstalledAppVersion}; store-latest unknown (no version poll yet)"
                    : $"already current: installed {probe.InstalledAppVersion}, store-latest {storeLatest}";
                logger.LogInformation("device check-update: {Id} skip store-drive ({Note})", id, note);
                jobs.Finish(id, new StoreCheckResult(
                    probe.Reachable, probe.InstalledAppVersion, probe.InstalledAppVersion, false, false, "up_to_date", note));
                return;
            }

            var result = await checker.CheckAndUpdateAsync(target, CancellationToken.None, msg => jobs.Progress(id, msg));

            if (result.Installed)
                await store.RecordUpdateAsync(new DeviceUpdate
                {
                    DeviceId = id, AttemptedAt = DateTimeOffset.UtcNow,
                    FromVersion = result.InstalledBefore, ToVersion = result.InstalledAfter,
                    Status = "verified", Note = result.Note, TriggeredBy = $"check:{who}",
                }, CancellationToken.None);

            var device = await store.GetAsync(id);
            if (device is not null)
            {
                var time = sp.GetRequiredService<TimeProvider>();
                await DeviceProbeRunner.ProbeOneAsync(
                    device, $"check-update:{who}", runner, store, db, logger, time, CancellationToken.None);
            }

            jobs.Finish(id, result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "device check-update: {Id} background run failed", id);
            jobs.Fail(id, ex.Message);
        }
    }

    // Live status of an in-flight (or just-finished) check-update. The UI polls this every ~3s while running.
    // Returns { state:"idle" } when no live/recent job (none started, or the terminal verdict's TTL elapsed).
    [HttpGet("{id}/check-status")]
    public IActionResult CheckStatus(string id)
    {
        if (RequireAdmin() is { } no) return no;
        var s = jobs.Get(id);
        if (s is null) return Ok(new { state = "idle" });
        return Ok(new
        {
            state = s.State.ToString().ToLowerInvariant(),
            message = s.Message, action = s.Action,
            installedBefore = s.InstalledBefore, installedAfter = s.InstalledAfter,
            startedAt = s.StartedAt, updatedAt = s.UpdatedAt,
        });
    }

    // Pull the installed app off the device and carve its proto in-process, then upsert a registry row.
    // Android: `adb pull` the arm split, run ArchiveProtoExtractor. iOS: ssh-pull the egginc Mach-O (only
    // the first __TEXT page is FairPlay-encrypted, so the on-disk binary carves with no runtime decrypt).
    [HttpPost("{id}/save")]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> Save(string id)
    {
        if (RequireAdmin() is { } no) return no;
        var store = Store;
        var registry = services.GetService(typeof(ProtoRegistryStore)) as ProtoRegistryStore;
        if (store is null || Db is null || registry is null) return StatusCode(503, new { error = "no database configured" });

        var logger = (ILogger<DevicesController>)services.GetRequiredService(typeof(ILogger<DevicesController>));
        var who = currentUser.DiscordId ?? "?";

        var device = await store.GetAsync(id);
        if (device is null) return NotFound(new { error = "unknown device" });
        if (device.Platform is not (PlatformAndroid or PlatformIos))
            return StatusCode(501, new { error = $"no extractor for platform {device.Platform}" });

        logger.LogInformation("device save: {Id} start (by {Who})", id, who);

        var runner = (IProcessRunner)services.GetRequiredService(typeof(IProcessRunner));
        var probe = await DeviceProbeRunner.ProbeFor(device, runner).ProbeAsync(HttpContext.RequestAborted);
        // Android needs both appVersion and build; iOS has no build (sha stands in), so only appVersion is required.
        var needBuild = device.Platform == PlatformAndroid;
        if (!probe.Reachable || string.IsNullOrEmpty(probe.InstalledAppVersion) || (needBuild && string.IsNullOrEmpty(probe.InstalledBuild)))
        {
            logger.LogWarning("device save: {Id} aborted: unreachable or no version ({Note})", id, probe.Note);
            return StatusCode(502, new { error = $"device unreachable or no version read: {probe.Note}" });
        }

        var (carve, err) = await PullAndCarveAsync(device, probe, runner, logger);
        if (err is not null) return err;

        var appVersion = probe.InstalledAppVersion!;
        var build = carve!.Build;
        var sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(carve.Proto))).ToLowerInvariant();

        // clientVersion is not in the binary; it is only reported on the wire, so launch the app and harvest
        // a fresh rinfo. Best-effort: a miss leaves it null.
        var clientVersion = await HarvestClientVersionAsync(device, HttpContext.RequestAborted);
        logger.LogInformation("device save: {Id} harvested clientVersion={Cv}", id, clientVersion?.ToString() ?? "(none)");

        try
        {
            var (row, created, protoChanged) = await registry.UpsertAsync(
                device.Platform, appVersion, build, clientVersion: clientVersion, package: device.Package,
                protoSha: sha, apkRef: $"device:{device.Id}", detectedAt: DateTimeOffset.UtcNow,
                detectedBy: $"device-save:{who}", protoText: carve.Proto, source: "device",
                resurrect: true, ct: HttpContext.RequestAborted);
            logger.LogInformation("device save: {Id} -> registry {Plat} build {Build} ({State}, sha {Sha})",
                id, device.Platform, build, created ? "created" : "updated", sha[..12]);

            // Fan the new/changed build out to feed subscriptions. Best-effort; no subs = no-op.
            var dispatcher = services.GetService(typeof(EggIncognito.Services.Feed.FeedDispatcher))
                as EggIncognito.Services.Feed.FeedDispatcher;
            if (dispatcher is not null)
            {
                var cfg = services.GetService(typeof(IConfiguration)) as IConfiguration;
                var pageUrl = EggIncognito.Services.Feed.FeedDispatcher.BuildPageUrl(
                    cfg?["Feed:PageBaseUrl"], device.Platform, build);
                await dispatcher.DispatchAsync(row.Id, device.Platform, appVersion, build, clientVersion: null,
                    sha, created, protoChanged, pageUrl, HttpContext.RequestAborted);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "device save: {Id} registry upsert failed for build {Build}", id, build);
            return StatusCode(500, new { error = $"registry write failed: {ex.Message}" });
        }

        var db = Db!;
        var time = (TimeProvider)services.GetRequiredService(typeof(TimeProvider));
        var reprobe = await DeviceProbeRunner.ProbeOneAsync(
            device, $"admin-save:{who}", runner, store, db, logger, time, HttpContext.RequestAborted);

        return Ok(new { saved = true, appVersion, build, result = reprobe.Result });
    }

    private const string PlatformAndroid = "android";
    private const string PlatformIos = "ios";

    private sealed record CarveResult(string Proto, string Build);

    // Per-platform pull + carve. Returns the carved proto text and registry build key on success,
    // or (null, errorResult) with the failure already logged.
    private async Task<(CarveResult? carve, IActionResult? err)> PullAndCarveAsync(
        Device device, DeviceProbeResult probe, IProcessRunner runner, ILogger logger)
    {
        if (device.Platform == PlatformAndroid)
        {
            var apk = await new DeviceApkPuller(runner).PullArmSplitAsync(device.Target, device.Package, HttpContext.RequestAborted);
            if (apk is null)
            {
                logger.LogWarning("device save: {Id} aborted: arm split pull failed", device.Id);
                return (null, StatusCode(502, new { error = "could not pull the arm split apk from the device" }));
            }
            logger.LogInformation("device save: {Id} pulled arm split ({Bytes} bytes), carving proto", device.Id, apk.Length);
            var carved = EggIncognito.Services.ProtoExtract.ArchiveProtoExtractor.Extract(apk);
            if (!carved.Ok || string.IsNullOrEmpty(carved.Proto))
            {
                logger.LogWarning("device save: {Id} carve failed: {Diag}", device.Id, carved.Diagnostics);
                return (null, StatusCode(500, new { error = $"proto carve failed: {carved.Diagnostics}" }));
            }
            return (new CarveResult(carved.Proto, probe.InstalledBuild!), null);
        }

        var s = (services.GetRequiredService(typeof(IConfiguration)) as IConfiguration)!
            .GetSection("DeviceUpdate").GetSection("Ios");
        var host = string.IsNullOrEmpty(s["SshHost"]) ? device.Target : s["SshHost"]!;
        var port = s["SshPort"] ?? "2222";
        var key = s["SshKeyPath"];
        if (string.IsNullOrEmpty(key))
        {
            logger.LogWarning("device save: {Id} aborted: ios ssh key not configured (DeviceUpdate:Ios:SshKeyPath)", device.Id);
            return (null, StatusCode(503, new { error = "ios extraction needs DeviceUpdate:Ios:SshKeyPath configured" }));
        }
        var bin = await new IosBinaryPuller(runner, host, port, key).PullBinaryAsync(device.Package, HttpContext.RequestAborted);
        if (bin is null)
        {
            logger.LogWarning("device save: {Id} aborted: ios binary pull failed", device.Id);
            return (null, StatusCode(502, new { error = "could not pull the egginc binary from the device over ssh" }));
        }
        logger.LogInformation("device save: {Id} pulled ios binary ({Bytes} bytes), carving proto", device.Id, bin.Length);

        // Stash the pulled binary where the eggincognito-runner-ios sidecar's IOS_BINARY_PATH reads it, so its
        // next poll tick emits a NewVersionEvent without needing its own device/ssh access. No-op if unset.
        var stashPath = (services.GetService(typeof(IConfiguration)) as IConfiguration)?["Runner:IosBinaryStashPath"];
        if (!string.IsNullOrEmpty(stashPath))
        {
            try { await System.IO.File.WriteAllBytesAsync(stashPath, bin, HttpContext.RequestAborted); }
            catch (Exception ex) { logger.LogWarning(ex, "device save: {Id} could not stash ios binary to {Path}", device.Id, stashPath); }
        }
        var iosCarve = EggIncognito.Services.ProtoExtract.MachoProtoExtractor.Extract(bin);
        if (!iosCarve.Ok || string.IsNullOrEmpty(iosCarve.Proto))
        {
            logger.LogWarning("device save: {Id} carve failed: {Diag}", device.Id, iosCarve.Diagnostics);
            return (null, StatusCode(500, new { error = $"proto carve failed: {iosCarve.Diagnostics}" }));
        }
        // iOS registry build = CFBundleVersion, matching what the probe reads and the Devices panel shows.
        // Falls back to the binary content sha only if the probe could not read CFBundleVersion.
        var iosBuild = !string.IsNullOrEmpty(probe.InstalledBuild)
            ? probe.InstalledBuild!
            : Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bin))[..16].ToLowerInvariant();
        return (new CarveResult(iosCarve.Proto, iosBuild), null);
    }

    // Harvest the on-the-wire clientVersion for the registry row: launch the app and wait for a fresh rinfo
    // via ForceHarvestAsync. Best-effort; capture off, no device entry, or no fresh rinfo returns null.
    private async Task<string?> HarvestClientVersionAsync(Device device, CancellationToken ct)
    {
        var pusher = services.GetService(typeof(DeviceProxyPusher)) as DeviceProxyPusher;
        var devCfg = services.GetService(typeof(DeviceConfig)) as DeviceConfig;
        if (pusher is null || devCfg is null) return null;
        var entry = devCfg.Devices.FirstOrDefault(d => d.Id == device.Id);
        if (entry is null) return null;

        var rinfo = await pusher.ForceHarvestAsync(entry, TimeSpan.FromSeconds(5), ct);
        return rinfo?.ClientVersion?.ToString();
    }

    // Resolve the iOS ssh connection from DeviceUpdate:Ios config. Host defaults to the device target.
    // Returns null when no ssh key is configured.
    private (string Host, string Port, string Key)? IosSsh(Device device)
    {
        var cfg = (services.GetService(typeof(IConfiguration)) as IConfiguration)!
            .GetSection("DeviceUpdate").GetSection("Ios");
        var key = cfg["SshKeyPath"];
        if (string.IsNullOrEmpty(key)) return null;
        var host = string.IsNullOrEmpty(cfg["SshHost"]) ? device.Target : cfg["SshHost"]!;
        return (host, cfg["SshPort"] ?? "2222", key);
    }

    // Pull the 3D ship meshes off the device and decode them to glTF (.glb). Android: pull base.apk (ship
    // meshes live in its assets, not the arm split the proto carve uses). iOS: ssh-tar the rpos files out
    // of the on-disk .app bundle. Returns the same manifest shape as POST /api/tools/extract-meshes.
    [HttpPost("{id}/pull-meshes")]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> PullMeshes(string id, [FromQuery] bool export = false, [FromQuery] string? build = null)
    {
        if (RequireAdmin() is { } no) return no;
        var store = Store;
        if (store is null) return StatusCode(503, new { error = "no database configured" });

        var device = await store.GetAsync(id);
        if (device is null) return NotFound(new { error = "unknown device" });
        if (device.Platform is not (PlatformAndroid or PlatformIos))
            return StatusCode(501, new { error = $"no mesh puller for platform {device.Platform}" });

        var runner = (IProcessRunner)services.GetRequiredService(typeof(IProcessRunner));
        var ct = HttpContext.RequestAborted;

        Services.ProtoExtract.RpoAssetExtractor.ExtractResult extract;
        if (device.Platform == PlatformAndroid)
        {
            var apk = await new DeviceApkPuller(runner).PullBaseSplitAsync(device.Target, device.Package, ct);
            if (apk is null) return StatusCode(502, new { error = "could not pull base.apk from the device" });
            extract = Services.ProtoExtract.RpoAssetExtractor.Extract(apk);
        }
        else
        {
            if (IosSsh(device) is not { } ssh)
                return StatusCode(503, new { error = "ios mesh pull needs DeviceUpdate:Ios:SshKeyPath configured" });
            var tar = await new IosAssetPuller(runner, ssh.Host, ssh.Port, ssh.Key).PullRposTarAsync(device.Package, ct);
            if (tar is null) return StatusCode(502, new { error = "could not pull the rpos meshes from the device over ssh" });
            var entries = Services.ProtoExtract.TarReader.Read(tar)
                .Select(e => (e.Name, e.Bytes));
            extract = Services.ProtoExtract.RpoAssetExtractor.FromEntries(entries);
        }

        // export=true returns the Spaceship-enum-keyed ship .glb set; otherwise the raw per-mesh manifest.
        return Ok(export ? MeshManifest.Ships(extract, build, false, null) : MeshManifest.From(extract));
    }

    // Lists the mesh (.rpo/.rpoz) file stems available on the device, names only, no decode. iOS: ssh `find`
    // in the .app bundle. Android: pull base.apk once and list its rpo entries.
    [HttpGet("{id}/list-meshes")]
    [EnableRateLimiting("read")]
    public async Task<IActionResult> ListMeshes(string id)
    {
        if (RequireAdmin() is { } no) return no;
        var store = Store;
        if (store is null) return StatusCode(503, new { error = "no database configured" });
        var device = await store.GetAsync(id);
        if (device is null) return NotFound(new { error = "unknown device" });

        var runner = (IProcessRunner)services.GetRequiredService(typeof(IProcessRunner));
        var ct = HttpContext.RequestAborted;

        if (device.Platform == PlatformIos)
        {
            if (IosSsh(device) is not { } ssh)
                return StatusCode(503, new { error = "ios mesh listing needs DeviceUpdate:Ios:SshKeyPath configured" });
            var names = await new IosAssetPuller(runner, ssh.Host, ssh.Port, ssh.Key).ListRposAsync(device.Package, ct);
            return Ok(new { meshes = names });
        }
        if (device.Platform == PlatformAndroid)
        {
            var apk = await new DeviceApkPuller(runner).PullBaseSplitAsync(device.Target, device.Package, ct);
            if (apk is null) return StatusCode(502, new { error = "could not pull base.apk from the device" });
            var names = Services.ProtoExtract.RpoAssetLister.ListStems(apk);
            return Ok(new { meshes = names });
        }
        return StatusCode(501, new { error = $"no mesh listing for platform {device.Platform}" });
    }

    // Pulls one mesh by stem off the device, decodes to glTF (.glb), optionally bakes an animation.
    [HttpGet("{id}/mesh/{stem}")]
    [EnableRateLimiting("read")]
    public async Task<IActionResult> Mesh(string id, string stem, [FromQuery] string? animate, [FromQuery] float seconds)
    {
        if (RequireAdmin() is { } no) return no;
        var ct = HttpContext.RequestAborted;

        // Cache-first pull; animation is applied on top so one cached glb serves every animation kind.
        var provider = (DeviceMeshProvider)services.GetRequiredService(typeof(DeviceMeshProvider));
        var res = await provider.GetGlbAsync(stem, id, ct);
        if (!res.Ok) return StatusCode(res.Status, new { error = res.Diagnostics });
        var glb = res.Glb!;

        if (!string.IsNullOrEmpty(animate))
        {
            var opts = new Services.Assets.GltfAnimator.Options(
                Services.Assets.GltfAnimator.ParseKind(animate), seconds > 0 ? seconds : 6f);
            var anim = Services.Assets.GltfAnimator.Animate(glb, opts);
            if (anim.Ok) glb = anim.Glb!;
        }
        return File(glb, "model/gltf-binary", $"{stem}.glb");
    }

    // Pre-computes every mesh on the device into the on-disk glb cache, so later /mesh requests serve from
    // cache. Pulls the archive once, decodes all, writes each un-animated glb. Needs ShipAssets:OutputDir
    // configured, else the cache is disabled and this is a no-op.
    [HttpPost("{id}/precache-meshes")]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> PrecacheMeshes(string id)
    {
        if (RequireAdmin() is { } no) return no;
        var store = Store;
        if (store is null) return StatusCode(503, new { error = "no database configured" });
        var device = await store.GetAsync(id);
        if (device is null) return NotFound(new { error = "unknown device" });

        var cache = services.GetService(typeof(MeshAssetCache)) as MeshAssetCache;
        if (cache is null || !cache.Enabled)
            return StatusCode(503, new { error = "mesh cache needs ShipAssets:OutputDir configured" });

        var runner = (IProcessRunner)services.GetRequiredService(typeof(IProcessRunner));
        var ct = HttpContext.RequestAborted;

        Services.ProtoExtract.RpoAssetExtractor.ExtractResult extract;
        if (device.Platform == PlatformAndroid)
        {
            var apk = await new DeviceApkPuller(runner).PullBaseSplitAsync(device.Target, device.Package, ct);
            if (apk is null) return StatusCode(502, new { error = "could not pull base.apk from the device" });
            extract = Services.ProtoExtract.RpoAssetExtractor.Extract(apk);
        }
        else if (device.Platform == PlatformIos)
        {
            if (IosSsh(device) is not { } ssh)
                return StatusCode(503, new { error = "ios mesh pull needs DeviceUpdate:Ios:SshKeyPath configured" });
            var tar = await new IosAssetPuller(runner, ssh.Host, ssh.Port, ssh.Key).PullRposTarAsync(device.Package, ct);
            if (tar is null) return StatusCode(502, new { error = "could not pull the rpos meshes over ssh" });
            extract = Services.ProtoExtract.RpoAssetExtractor.FromEntries(
                Services.ProtoExtract.TarReader.Read(tar).Select(e => (e.Name, e.Bytes)));
        }
        else return StatusCode(501, new { error = $"no mesh pull for platform {device.Platform}" });

        var cached = 0;
        var failed = new List<string>();
        foreach (var asset in extract.Assets)
        {
            if (asset.Decode.Ok && asset.Decode.Glb is { } g)
            {
                await cache.PutAsync(device.Platform, asset.Key, g, ct);
                cached++;
            }
            else failed.Add(asset.Key);
        }
        return Ok(new { ok = true, platform = device.Platform, cached, failed = failed.Count, failedKeys = failed.Take(20) });
    }

    // Lists the cached meshes for a device's platform (stem + size + time).
    [HttpGet("{id}/cached-meshes")]
    [EnableRateLimiting("read")]
    public async Task<IActionResult> CachedMeshes(string id)
    {
        if (RequireAdmin() is { } no) return no;
        var device = await Store?.GetAsync(id)!;
        if (device is null) return NotFound(new { error = "unknown device" });
        var cache = services.GetService(typeof(MeshAssetCache)) as MeshAssetCache;
        if (cache is null || !cache.Enabled) return Ok(new { enabled = false, meshes = Array.Empty<object>() });
        var meshes = cache.List(device.Platform).Select(m => new { stem = m.Stem, bytes = m.Bytes, cachedAt = m.CachedAt });
        return Ok(new { enabled = true, platform = device.Platform, meshes });
    }

    // Deletes one cached mesh by stem, or all when stem is "*".
    [HttpDelete("{id}/cached-meshes/{stem}")]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> DeleteCachedMesh(string id, string stem)
    {
        if (RequireAdmin() is { } no) return no;
        var device = await Store?.GetAsync(id)!;
        if (device is null) return NotFound(new { error = "unknown device" });
        var cache = services.GetService(typeof(MeshAssetCache)) as MeshAssetCache;
        if (cache is null || !cache.Enabled) return StatusCode(503, new { error = "mesh cache not configured" });

        if (stem == "*")
        {
            var n = cache.Clear(device.Platform);
            return Ok(new { ok = true, cleared = n });
        }
        var deleted = cache.Delete(device.Platform, stem);
        return Ok(new { ok = deleted, deleted });
    }

    // Force-restart the egginc app on the device so it makes a fresh launch request to auxbrain, which the
    // capture proxy decrypts to harvest rinfo. Needed because an idle/backgrounded app won't re-hit auxbrain.
    [HttpPost("{id}/restart-app")]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> RestartApp(string id)
    {
        if (RequireAdmin() is { } no) return no;
        if (services.GetService(typeof(EggIncognito.Services.Devices.DeviceProxyPusher))
                is not EggIncognito.Services.Devices.DeviceProxyPusher pusher
            || services.GetService(typeof(EggIncognito.Services.Devices.DeviceConfig))
                is not EggIncognito.Services.Devices.DeviceConfig devCfg)
            return StatusCode(503, new { error = "device capture not configured" });

        var entry = devCfg.Devices.FirstOrDefault(d => d.Id == id);
        if (entry is null) return NotFound(new { error = "unknown device" });

        var (ok, note) = await pusher.RestartAppAsync(entry, HttpContext.RequestAborted);
        return ok ? Ok(new { restarted = true, note }) : StatusCode(502, new { error = note ?? "restart failed" });
    }

    // Latest live rinfo harvested off the wire for a device (build/clientVersion/version + recency).
    // Empty 200 when none seen or capture is off.
    [HttpGet("{id}/live")]
    [EnableRateLimiting("fetch")] // public + polled per-device by DeviceStatusPanel; not the class "read" cap
    public IActionResult Live(string id)
    {
        if (services.GetService(typeof(DeviceCaptureManager)) is not DeviceCaptureManager mgr)
            return Ok(new { found = false });
        var d = mgr.DiagFor(id);
        var capture = new
        {
            listening = mgr.PortFor(id) != 0,
            port = mgr.PortFor(id),
            clientConnects = d.ClientConnects,
            auxbrainConnects = d.AuxbrainConnects,
            flows = d.Flows,
            rinfoHarvests = d.RinfoHarvests,
            lastDecryptError = d.LastDecryptError,
            recentConnects = d.RecentConnects,
        };
        var v = mgr.Rinfo.Latest(id);
        if (v is null) return Ok(new { found = false, capture });
        return Ok(new { found = true, v.DeviceId, v.Platform, v.Version, v.Build, v.ClientVersion, v.LastSeen, capture });
    }
}
