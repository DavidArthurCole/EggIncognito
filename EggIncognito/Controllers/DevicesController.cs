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
    public async Task<IActionResult> Status()
    {
        var store = Store;
        if (store is null) return Ok(Array.Empty<object>());
        var latest = await store.LatestPerDeviceAsync();
        var devices = (await store.EnabledDevicesAsync()).ToDictionary(d => d.Id);
        var updates = (await store.LatestUpdatePerDeviceAsync()).ToDictionary(u => u.DeviceId);

        // store-latest per distinct platform, so the Update button can show only when the store is ahead of
        // what is installed. One lookup per platform (android/ios), reused across that platform's devices.
        var db = Db;
        var storeLatest = new Dictionary<string, string?>();
        // Live REGISTRY latest per platform (non-deleted), used to RE-CLASSIFY at read time. The stored probe
        // Result is a snapshot from probe time; if a registry entry is later deleted, the installed version is
        // no longer represented and the row must flip back to new_version (re-surfacing the Save button) WITHOUT
        // waiting for the next probe tick. So we recompute new_version/no_change against the current registry.
        var regLatestApp = new Dictionary<string, string?>();
        var regLatestBuild = new Dictionary<string, string?>();
        if (db is not null)
            foreach (var plat in devices.Values.Select(d => d.Platform).Distinct())
            {
                storeLatest[plat] = await StoreAheadCheck.StoreLatestAsync(db, plat, HttpContext.RequestAborted);
                // "Represented" = a live row OR a merged alias. A merge hides the iOS row (DeletedAt set,
                // CanonicalId set) under a cross-platform canonical, but the proto IS still captured for that
                // platform, so the device must NOT re-flag it as new_version. Only TRUE soft-deletes
                // (DeletedAt set, CanonicalId null) drop a version out of "represented".
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
            // Reclassify against the live registry so a deleted entry re-surfaces the Save button immediately.
            // Falls back to the stored Result for unreachable/error rows (where reclassification is moot).
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

    // Tell the plugged-in device to ASK ITS OWN STORE for an Egg Inc update and install it if there is one.
    // Android: adb drives the on-device Play Store. iOS: ssh fires the eggupdate tweak (on-device App Store).
    // The device's store is the source of truth; the server only nudges + polls the installed version.
    //
    // FIRE-AND-FORGET: the store poll runs ~6 minutes, far past the reverse-proxy timeout, so we validate +
    // launch a background task and return 202 immediately. The UI polls GET check-status for live progress and
    // the final verdict. One overlap guard per device (the tracker's TryStart). Admin + DB gated.
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

        // Run the long poll detached. A FRESH DI scope: the request scope is disposed once the 202 response
        // completes, and HttpContext.RequestAborted fires on response completion (would cancel us instantly).
        // So: own scope, CancellationToken.None, resolve everything from the scope.
        _ = Task.Run(() => RunCheckUpdateAsync(id, target, checker, who));

        return Accepted(new { id = device.Id, action = "running" });
    }

    // Background body of check-update. Owns its DI scope and lifetime; never touches request-scoped state. Every
    // exit funnels through the tracker (Finish/Fail) so the UI's check-status poll always reaches a terminal row.
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

            // Pre-check: only drive the device store when the store is actually ahead of what is installed.
            // The checkers detect ANY version climb (they can't know the store target), so without this they
            // fire the trigger + poll the full ~6 min window even when the device is already current. The
            // store-latest (known_versions, populated by VersionPollerService) lets us skip that no-op wait.
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

            // Record an update row when something actually moved, so history reflects the check.
            if (result.Installed)
                await store.RecordUpdateAsync(new DeviceUpdate
                {
                    DeviceId = id, AttemptedAt = DateTimeOffset.UtcNow,
                    FromVersion = result.InstalledBefore, ToVersion = result.InstalledAfter,
                    Status = "verified", Note = result.Note, TriggeredBy = $"check:{who}",
                }, CancellationToken.None);

            // Re-probe so the card reflects the post-check state.
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

    // Pull the installed app off the device, carve its proto in-process (pure C#, no python toolchain),
    // and upsert a registry row. Android: `adb pull` the arm split, run ArchiveProtoExtractor. iOS: ssh-pull
    // the egginc Mach-O (only the first __TEXT page is FairPlay-encrypted; the FileDescriptorProto blobs
    // live past it in __DATA, so the on-disk binary carves with no runtime decrypt), run MachoProtoExtractor.
    // iOS has no versionCode, so build = the binary's content sha (mirrors IosRunner). Admin + DB gated.
    // Every step logs so a failure is diagnosable from the server log instead of vanishing into a silent 500.
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
        // Android needs both appVersion + build; iOS has no build (sha stands in) so only appVersion required.
        var needBuild = device.Platform == PlatformAndroid;
        if (!probe.Reachable || string.IsNullOrEmpty(probe.InstalledAppVersion) || (needBuild && string.IsNullOrEmpty(probe.InstalledBuild)))
        {
            logger.LogWarning("device save: {Id} aborted: unreachable or no version ({Note})", id, probe.Note);
            return StatusCode(502, new { error = $"device unreachable or no version read: {probe.Note}" });
        }

        // Pull + carve, per platform. carve.Proto carries proto TEXT either way.
        var (carve, err) = await PullAndCarveAsync(device, probe, runner, logger);
        if (err is not null) return err;

        var appVersion = probe.InstalledAppVersion!;
        var build = carve!.Build;
        var sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(carve.Proto))).ToLowerInvariant();

        // clientVersion is NOT in the binary; it is only reported by the app on the wire (rinfo). Launch the
        // app + capture a fresh rinfo (~5s window) to harvest it, so the registry row carries the real
        // clientVersion instead of an empty field. Best-effort: a miss (capture off, app slow) leaves it null.
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

            // Fan the new/changed build out to feed subscriptions, same as the live-API sync path. Without
            // this a device-Save build never notified subscribers. Best-effort + DB-gated; no subs = no-op.
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

        // Re-probe so the card flips to no_change now that the build is extracted.
        var db = Db!;
        var time = (TimeProvider)services.GetRequiredService(typeof(TimeProvider));
        var reprobe = await DeviceProbeRunner.ProbeOneAsync(
            device, $"admin-save:{who}", runner, store, db, logger, time, HttpContext.RequestAborted);

        return Ok(new { saved = true, appVersion, build, result = reprobe.Result });
    }

    private const string PlatformAndroid = "android";
    private const string PlatformIos = "ios";

    private sealed record CarveResult(string Proto, string Build);

    // Per-platform pull + carve, factored out of Save to keep its complexity down. Returns the carved proto
    // text + the registry build key on success, or (null, errorResult) with the failure already logged.
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
            return (new CarveResult(carved.Proto, probe.InstalledBuild!), null); // versionCode authoritative
        }

        // ios: ssh-pull the Mach-O (first __TEXT page FairPlay-encrypted, proto blobs past it), carve.
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
        var iosCarve = EggIncognito.Services.ProtoExtract.MachoProtoExtractor.Extract(bin);
        if (!iosCarve.Ok || string.IsNullOrEmpty(iosCarve.Proto))
        {
            logger.LogWarning("device save: {Id} carve failed: {Diag}", device.Id, iosCarve.Diagnostics);
            return (null, StatusCode(500, new { error = $"proto carve failed: {iosCarve.Diagnostics}" }));
        }
        // iOS registry build = CFBundleVersion (e.g. 1.36.0.2), the SAME value the probe reads + the Devices
        // panel shows. This is the canonical iOS build (owner decision): the registry row must match what the
        // device row displays, not an opaque hash. Fall back to the binary content sha ONLY if the probe could
        // not read CFBundleVersion (e.g. CSV-form ideviceinstaller), so Save still never hard-blocks.
        // (The auxbrain wire build 1113xx, harvested via capture, is a SEPARATE concept and not the row key.)
        var iosBuild = !string.IsNullOrEmpty(probe.InstalledBuild)
            ? probe.InstalledBuild!
            : Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bin))[..16].ToLowerInvariant();
        return (new CarveResult(iosCarve.Proto, iosBuild), null);
    }

    // Harvest the on-the-wire clientVersion for the registry row. clientVersion is NOT in the binary (it is the
    // proto/API version the running client reports), so the only source is a live rinfo. Launch the app + wait
    // for a fresh rinfo (~5s window via ForceHarvestAsync), return its clientVersion as a string. Best-effort:
    // capture off / no device entry / no fresh rinfo => null (the row's clientVersion stays empty, as before).
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

    // Latest live rinfo harvested off the wire for a device (build/clientVersion/version + recency). Public
    // read so the status panel can show the captured build to anyone. Empty 200 when none seen / capture off.
    [HttpGet("{id}/live")]
    public IActionResult Live(string id)
    {
        if (services.GetService(typeof(DeviceCaptureManager)) is not DeviceCaptureManager mgr)
            return Ok(new { found = false });
        var v = mgr.Rinfo.Latest(id);
        if (v is null) return Ok(new { found = false });
        return Ok(new { found = true, v.DeviceId, v.Platform, v.Version, v.Build, v.ClientVersion, v.LastSeen });
    }
}
