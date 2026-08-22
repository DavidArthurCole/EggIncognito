using EggIdentity.Contract;
using EggIncognito.Core;
using EggIncognito.Core.Services.Devices;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.Services;
using EggIncognito.Services.Auth;
using EggIncognito.Services.Devices;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Controllers;

[ApiController]
[Route("api/devices")]
[ApiAccess(ApiAccessLevel.Public)]
[EnableRateLimiting("read")]
public sealed class DevicesController(
    ICurrentUser currentUser,
    IServiceProvider services,
    IServiceScopeFactory scopeFactory) : ControllerBase {
    private IDeviceStatusStore? Store => services.GetService(typeof(IDeviceStatusStore)) as IDeviceStatusStore;
    private DeviceJobStore? Jobs => services.GetService(typeof(DeviceJobStore)) as DeviceJobStore;
    private DeviceTimelineCache? Timeline => services.GetService(typeof(DeviceTimelineCache)) as DeviceTimelineCache;
    private EggIncognitoDbContext? Db => services.GetService(typeof(EggIncognitoDbContext)) as EggIncognitoDbContext;

    private ObjectResult? RequireAdmin() =>
        currentUser.IsAtLeast(UserRole.Admin) ? null : StatusCode(403, new { error = "admin role required" });

    private static string DeviceKey(string realId) =>
        Hashes.Sha256HexShort(realId, 16);

    private async Task<string?> ResolveDeviceIdAsync(string incoming, CancellationToken ct) {
        if (currentUser.IsAtLeast(UserRole.Admin)) return incoming;
        var store = Store;
        if (store is null) return null;
        var enabled = await store.EnabledDevicesAsync(ct);
        return enabled.FirstOrDefault(d => DeviceKey(d.Id) == incoming)?.Id;
    }

    [HttpGet("status")]
    [EnableRateLimiting("fetch")]
    public async Task<IActionResult> Status() {
        var store = Store;
        if (store is null || Timeline is not { } timeline) return Ok(Array.Empty<object>());
        var devices = (await store.EnabledDevicesAsync()).ToDictionary(d => d.Id);
        var ids = devices.Keys.ToList();
        var ct0 = HttpContext.RequestAborted;
        var latest = await timeline.LatestPerDeviceAsync(ids, DeviceJobKinds.Probe, ct0);
        var updates = (await timeline.LatestPerDeviceAsync(ids, DeviceJobKinds.StoreCheck, ct0))
            .ToDictionary(u => u.DeviceId);


        var db = Db;
        var storeLatest = new Dictionary<string, string?>();


        var regLatestApp = new Dictionary<string, string?>();
        var regLatestBuild = new Dictionary<string, string?>();
        var regClientVersion = new Dictionary<(string Platform, string AppVersion), int>();
        var regBuildClientVersion = new Dictionary<(string Platform, string Build), int>();
        if (db is not null) {
            foreach (string plat in devices.Values.Select(d => d.Platform).Distinct()) {
                storeLatest[plat] = await StoreAheadCheck.StoreLatestAsync(db, plat, HttpContext.RequestAborted);


                var extracted = await db.ProtoVersions.AsNoTracking()
                    .Where(v => v.Platform == plat && (v.DeletedAt == null || v.CanonicalId != null))
                    .Select(v => new { v.Build, v.AppVersion, v.ClientVersion })
                    .ToListAsync(HttpContext.RequestAborted);
                foreach (var e in extracted.OrderBy(v => v.Build,
                             Comparer<string>.Create((x, y) => DeviceParsing.CompareVersions(x, y)))) {
                    if (!int.TryParse(e.ClientVersion, out int cvv)) continue;
                    if (!string.IsNullOrEmpty(e.AppVersion)) regClientVersion[(plat, e.AppVersion)] = cvv;
                    if (!string.IsNullOrEmpty(e.Build)) regBuildClientVersion[(plat, e.Build)] = cvv;
                }

                regLatestApp[plat] = extracted.Select(e => e.AppVersion)
                    .OrderByDescending(v => v, Comparer<string>.Create((x, y) => DeviceParsing.CompareVersions(x, y)))
                    .FirstOrDefault();
                regLatestBuild[plat] = Platforms.Matches(plat, Platforms.Android)
                    ? extracted.Select(e => e.Build).Where(b => long.TryParse(b, out _)).OrderByDescending(long.Parse)
                        .FirstOrDefault()
                    : null;
            }
        }

        bool isAdmin = currentUser.IsAtLeast(UserRole.Admin);
        var binaries = services.GetService(typeof(GameBinaryProvider)) as GameBinaryProvider;

        var capturedClientVersion = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (services.GetService(typeof(DeviceStateStore)) is DeviceStateStore deviceStates) {
            foreach (var s in await deviceStates.ListAsync(HttpContext.RequestAborted)) {
                if (s.ClientVersion is { } scv) capturedClientVersion[s.DeviceId] = scv;
            }
        }

        var rows = latest.Where(p => devices.ContainsKey(p.DeviceId)).Select(p => {
            var d = devices[p.DeviceId];
            updates.TryGetValue(d.Id, out var up);
            string? sl = storeLatest.GetValueOrDefault(d.Platform);

            string liveResult = p.Reachable == true && !string.IsNullOrEmpty(p.AppVersion)
                ? DeviceProbeRunner.Classify(
                    new DeviceProbeResult(true, p.AppVersion, p.Build, null),
                    d.Platform, regLatestBuild.GetValueOrDefault(d.Platform),
                    regLatestApp.GetValueOrDefault(d.Platform))
                : p.Outcome ?? "";
            return new {
                id = isAdmin ? d.Id : DeviceKey(d.Id),
                platform = d.Platform,
                label = d.Label,
                target = isAdmin ? d.Target : null,
                package = isAdmin ? d.Package : null,
                reachable = p.Reachable == true,
                installedAppVersion = p.AppVersion,
                installedBuild = p.Build,
                clientVersion = binaries?.CachedClientVersion(d.Platform, p.AppVersion)
                    ?? (p.Build is { } ib &&
                        regBuildClientVersion.TryGetValue((d.Platform, ib), out int bcv)
                        ? bcv
                        : p.AppVersion is { } iv &&
                          regClientVersion.TryGetValue((d.Platform, iv), out int rcv)
                            ? rcv
                            : capturedClientVersion.TryGetValue(d.Id, out int ccv)
                                ? ccv
                                : (int?)null),
                storeLatest = sl,
                storeAhead = StoreAheadCheck.IsAhead(sl, p.AppVersion),
                result = liveResult,
                note = isAdmin ? p.Message : null,
                probedAt = p.StartedAt,
                lastUpdate = !isAdmin || up is null
                    ? null
                    : new {
                        status = up.Outcome,
                        note = up.Message,
                        by = up.Trigger,
                        at = up.StartedAt
                    }
            };
        });
        return Ok(rows);
    }

    [HttpGet("{id}/history")]
    [ApiAccess(ApiAccessLevel.Admin)]
    public async Task<IActionResult> History(string id, [FromQuery] int n = 20) {
        if (RequireAdmin() is { } no) return no;
        if (Timeline is not { } timeline) return Ok(Array.Empty<object>());
        var rows = await timeline.HistoryAsync(id, Math.Clamp(n, 1, 100), DeviceJobKinds.Probe,
            HttpContext.RequestAborted);
        return Ok(rows.Select(p => new {
            probedAt = p.StartedAt,
            reachable = p.Reachable == true,
            installedAppVersion = p.AppVersion,
            installedBuild = p.Build,
            result = p.Outcome ?? "",
            triggeredBy = p.Trigger,
            note = p.Message
        }));
    }

    [HttpGet("{id}/jobs")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [EnableRateLimiting("read")]
    public async Task<IActionResult> JobHistory(string id, [FromQuery] int n = 50, [FromQuery] string? kind = null,
        CancellationToken ct = default) {
        if (RequireAdmin() is { } no) return no;
        if (Timeline is not { } cache) return StatusCode(503, new { error = "no database configured" });

        var rows = await cache.HistoryAsync(id, Math.Clamp(n, 1, 200), kind, ct);
        var outRows = new List<object>();
        foreach (var j in rows) {
            var lines = await cache.LinesAsync(id, j.Id, ct);
            outRows.Add(new {
                id = j.Id,
                kind = j.Kind,
                state = j.State,
                trigger = j.Trigger,
                startedAt = j.StartedAt,
                finishedAt = j.FinishedAt,
                outcome = j.Outcome,
                message = j.Message,
                appVersion = j.AppVersion,
                build = j.Build,
                revision = j.Revision,
                detail = j.Detail,
                lines = lines.Select(l => new {
                    at = l.At,
                    level = l.Level,
                    text = l.Text,
                    entry = l.Entry,
                    bytes = l.Bytes,
                    sha256 = l.Sha256
                })
            });
        }

        return Ok(outRows);
    }

    [HttpGet("jobs/live")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [EnableRateLimiting("fetch")]
    public async Task<IActionResult> LiveJobs(CancellationToken ct) {
        if (RequireAdmin() is { } no) return no;
        var store = Store;
        if (store is null || Timeline is not { } cache) return Ok(Array.Empty<object>());

        var ids = (await store.EnabledDevicesAsync(ct)).Select(d => d.Id).ToList();
        var running = await cache.RunningAsync(ids, ct);
        return Ok(running.Select(j => new {
            device = j.DeviceId,
            id = j.Id,
            kind = j.Kind,
            message = j.Message,
            startedAt = j.StartedAt
        }));
    }

    [HttpPost("{id}/refresh")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> Refresh(string id) {
        if (RequireAdmin() is { } no) return no;

        if (services.GetService(typeof(IDeviceAgentClient)) is IDeviceAgentClient agent && agent.Enabled) {
            var dto = await agent.ProbeAsync(id, HttpContext.RequestAborted);
            if (dto is not null) {
                var agentDevice = await (Store?.GetAsync(id, HttpContext.RequestAborted) ?? Task.FromResult<Device?>(null));
                if (agentDevice is null) return NotFound(new { error = "unknown device" });
                return Ok(new {
                    id = dto.Id,
                    platform = agentDevice.Platform,
                    label = agentDevice.Label,
                    reachable = dto.Reachable,
                    installedAppVersion = dto.InstalledAppVersion,
                    installedBuild = dto.InstalledBuild,
                    latestAvailable = dto.LatestAvailable,
                    result = dto.Result,
                    note = dto.Note,
                    probedAt = dto.ProbedAt
                });
            }
        }

        var store = Store;
        var db = Db;
        if (store is null || db is null || Jobs is not { } jobStore)
            return StatusCode(503, new { error = "no database configured" });

        var device = await store.GetAsync(id);
        if (device is null) return NotFound(new { error = "unknown device" });

        var platforms = (IDevicePlatforms)services.GetRequiredService(typeof(IDevicePlatforms));
        var time = (TimeProvider)services.GetRequiredService(typeof(TimeProvider));
        var logger = (ILogger<DevicesController>)services.GetRequiredService(typeof(ILogger<DevicesController>));

        var row = await DeviceProbeRunner.ProbeOneAsync(
            device, $"admin:{currentUser.DiscordId}", platforms, jobStore, db, logger, time,
            HttpContext.RequestAborted);

        return Ok(new {
            id = device.Id,
            platform = device.Platform,
            label = device.Label,
            reachable = row.Reachable == true,
            installedAppVersion = row.AppVersion,
            installedBuild = row.Build,
            result = row.Outcome ?? "",
            note = row.Message,
            probedAt = row.StartedAt
        });
    }


    [HttpPost("refresh-all")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> RefreshAll() {
        if (RequireAdmin() is { } no) return no;

        if (services.GetService(typeof(IDeviceAgentClient)) is IDeviceAgentClient agent && agent.Enabled) {
            int probedByAgent = await agent.ProbeAllAsync(HttpContext.RequestAborted);
            return Ok(new { probed = probedByAgent });
        }

        var store = Store;
        var db = Db;
        if (store is null || db is null || Jobs is not { } jobStore)
            return StatusCode(503, new { error = "no database configured" });

        var platforms = (IDevicePlatforms)services.GetRequiredService(typeof(IDevicePlatforms));
        var time = (TimeProvider)services.GetRequiredService(typeof(TimeProvider));
        var logger = (ILogger<DevicesController>)services.GetRequiredService(typeof(ILogger<DevicesController>));

        var devices = await store.EnabledDevicesAsync();
        int n = 0;
        foreach (var d in devices) {
            try {
                await DeviceProbeRunner.ProbeOneAsync(
                    d, $"admin-all:{currentUser.DiscordId}", platforms, jobStore, db, logger, time,
                    HttpContext.RequestAborted);
                n++;
            } catch (Exception ex) {
                logger.LogWarning(ex, "refresh-all: {Id} threw", d.Id);
            }
        }

        return Ok(new { probed = n });
    }


    [HttpPost("{id}/check-update")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> CheckUpdate(string id) {
        if (RequireAdmin() is { } no) return no;
        var store = Store;
        var db = Db;
        if (store is null || db is null) return StatusCode(503, new { error = "no database configured" });

        var logger = (ILogger<DevicesController>)services.GetRequiredService(typeof(ILogger<DevicesController>));
        string who = currentUser.DiscordId ?? "?";

        var device = await store.GetAsync(id);
        if (device is null) return NotFound(new { error = "unknown device" });

        var checker = services.GetServices<IDeviceStoreChecker>()
            .FirstOrDefault(c => string.Equals(c.Platform, device.Platform, StringComparison.OrdinalIgnoreCase));
        if (checker is null)
            return StatusCode(501, new { error = $"no store checker for platform {device.Platform}" });


        if (Jobs is not { } jobStore) return StatusCode(503, new { error = "no database configured" });
        var job = await jobStore.TryStartAsync(id, DeviceJobKinds.StoreCheck, $"admin:{who}", "checking store...",
            HttpContext.RequestAborted);
        if (job is null) return StatusCode(409, new { error = "another job is already running on this device" });

        logger.LogInformation("device check-update: {Id} start (by {Who})", id, who);
        var target = new DeviceTarget(device.Id, device.Platform, device.Target, device.Package);


        _ = Task.Run(() => RunCheckUpdateAsync(job, target, checker, who));

        return Accepted(new { id = device.Id, jobId = job.Id, action = "running" });
    }


    private async Task RunCheckUpdateAsync(JobRef job, DeviceTarget target, IDeviceStoreChecker checker,
        string who) {
        using var scope = scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var logger = sp.GetRequiredService<ILogger<DevicesController>>();
        var jobs = sp.GetRequiredService<DeviceJobStore>();
        try {
            var store = sp.GetService<IDeviceStatusStore>();
            var db = sp.GetService<EggIncognitoDbContext>();
            var platforms = sp.GetRequiredService<IDevicePlatforms>();
            if (store is null || db is null) {
                await jobs.FailAsync(job, "no database configured", CancellationToken.None);
                return;
            }


            var result = await checker.CheckAndUpdateAsync(target, CancellationToken.None,
                msg => jobs.ProgressAsync(job, msg).GetAwaiter().GetResult());

            var device = await store.GetAsync(job.DeviceId);
            if (device is not null) {
                var time = sp.GetRequiredService<TimeProvider>();
                await DeviceProbeRunner.ProbeOneAsync(
                    device, $"check-update:{who}", platforms, jobs, db, logger, time, CancellationToken.None);
            }

            await jobs.FinishAsync(job, result.Action, result.Note,
                new DeviceJobFacts(
                    AppVersion: result.InstalledAfter,
                    Detail: new { fromVersion = result.InstalledBefore, toVersion = result.InstalledAfter }),
                CancellationToken.None);
        } catch (Exception ex) {
            logger.LogError(ex, "device check-update: {Id} background run failed", job.DeviceId);
            await jobs.FailAsync(job, ex.Message, CancellationToken.None);
        }
    }


    [HttpPost("{id}/save")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> Save(string id) {
        if (RequireAdmin() is { } no) return no;
        if (services.GetService(typeof(DeviceRegistryPublisher)) is not DeviceRegistryPublisher publisher)
            return StatusCode(503, new { error = "no database configured" });

        string who = currentUser.DiscordId ?? "?";
        var res = await publisher.PublishAsync(id, $"device-save:{who}", true, HttpContext.RequestAborted);
        return res.Outcome switch {
            PublishOutcome.Published =>
                Ok(new { saved = true, appVersion = res.AppVersion, build = res.Build }),
            PublishOutcome.UnknownDevice => NotFound(new { error = res.Error }),
            PublishOutcome.UnsupportedPlatform => StatusCode(501, new { error = res.Error }),
            PublishOutcome.NotConfigured => StatusCode(503, new { error = res.Error }),
            PublishOutcome.NotHarvested or PublishOutcome.StaleHarvest or PublishOutcome.MissingAsset =>
                StatusCode(409, new { error = res.Error }),
            _ => StatusCode(500, new { error = res.Error })
        };
    }


    [HttpGet("{id}/list-meshes")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [EnableRateLimiting("read")]
    public async Task<IActionResult> ListMeshes(string id) {
        if (RequireAdmin() is { } no) return no;
        var store = Store;
        if (store is null) return StatusCode(503, new { error = "no database configured" });
        var device = await store.GetAsync(id);
        if (device is null) return NotFound(new { error = "unknown device" });
        if (services.GetService(typeof(DeviceAssetStore)) is not DeviceAssetStore assets)
            return StatusCode(503, new { error = "no database configured" });

        var heads = await assets.ListAsync(DeviceAssetKinds.Mesh, device.Platform, HttpContext.RequestAborted);
        return Ok(new { meshes = heads.Select(h => h.Name), harvested = heads.Count });
    }


    [HttpPost("{id}/poke")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> Poke(string id) {
        if (RequireAdmin() is { } no) return no;
        var store = Store;
        if (store is null) return StatusCode(503, new { error = "no database configured" });
        if (await store.GetAsync(id) is null) return NotFound(new { error = "unknown device" });
        if (services.GetService(typeof(IDeviceAgentClient)) is not IDeviceAgentClient { Enabled: true } agent)
            return StatusCode(503, new { error = "no device agent configured (set DeviceAgent:Url + DeviceAgent:Secret)" });

        bool queued = await agent.PokeAsync(id, true, HttpContext.RequestAborted);
        return queued
            ? Accepted(new { ok = true, device = id, queued = true })
            : StatusCode(502, new { error = "device agent did not accept the poke" });
    }


    [HttpPost("poke-all")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> PokeAll() {
        if (RequireAdmin() is { } no) return no;
        if (services.GetService(typeof(IDeviceAgentClient)) is not IDeviceAgentClient { Enabled: true } agent)
            return StatusCode(503, new { error = "no device agent configured (set DeviceAgent:Url + DeviceAgent:Secret)" });

        bool queued = await agent.PokeAsync(null, true, HttpContext.RequestAborted);
        return queued
            ? Accepted(new { ok = true, queued = true })
            : StatusCode(502, new { error = "device agent did not accept the poke" });
    }


    [HttpGet("{id}/harvest")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [EnableRateLimiting("read")]
    public async Task<IActionResult> Harvest(string id) {
        if (RequireAdmin() is { } no) return no;
        if (services.GetService(typeof(DeviceStateStore)) is not DeviceStateStore states)
            return StatusCode(503, new { error = "no database configured" });

        var ct = HttpContext.RequestAborted;
        var row = await states.GetAsync(id, ct);
        if (row is null) return NotFound(new { error = "no harvest state for device" });
        var cache = Timeline;
        var harvestJob = cache is null ? null : await cache.LatestAsync(id, DeviceJobKinds.Harvest, ct);
        IReadOnlyList<DeviceJobLineRow> entries = harvestJob is null || cache is null
            ? []
            : await cache.LinesAsync(id, harvestJob.Id, ct);
        bool inRegistry = !string.IsNullOrEmpty(row.Build)
                          && services.GetService(typeof(ProtoRegistryStore)) is ProtoRegistryStore registry
                          && await registry.GetAsync(row.Platform, row.Build, ct) is not null;
        return Ok(new {
            device = row.DeviceId,
            platform = row.Platform,
            appVersion = row.AppVersion,
            build = row.Build,
            inRegistry,
            revision = row.Revision,
            harvestedRevision = row.HarvestedRevision,
            stale = !string.Equals(row.Revision, row.HarvestedRevision, StringComparison.Ordinal),
            dirty = row.Dirty,
            harvesting = row.Harvesting,
            lastHarvestAt = row.LastHarvestAt,
            lastHarvestStatus = row.LastHarvestStatus,
            lastHarvestNote = row.LastHarvestNote,
            entries = entries.Select(e => {
                (string outcome, string? note) = SplitJobLine(e.Text);
                return new {
                    ranAt = e.At,
                    entry = e.Entry,
                    kind = (string?)null,
                    outcome,
                    note,
                    bytes = e.Bytes ?? 0,
                    sha256 = e.Sha256
                };
            })
        });
    }

    private static (string Outcome, string? Note) SplitJobLine(string? text) {
        if (string.IsNullOrWhiteSpace(text)) return ("ok", null);
        int sep = text.IndexOf(':', StringComparison.Ordinal);
        if (sep <= 0) return (text, null);
        string note = text[(sep + 1)..].Trim();
        return (text[..sep], note.Length == 0 ? null : note);
    }


    [HttpPost("{id}/restart-app")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> RestartApp(string id) {
        if (RequireAdmin() is { } no) return no;
        if (services.GetService(typeof(DeviceProxyPusher))
                is not DeviceProxyPusher pusher
            || services.GetService(typeof(DeviceConfig))
                is not DeviceConfig devCfg) {
            return StatusCode(503, new { error = "device capture not configured" });
        }

        var entry = devCfg.Devices.FirstOrDefault(d => d.Id == id);
        if (entry is null) return NotFound(new { error = "unknown device" });

        (bool ok, string? note) = await pusher.RestartAppAsync(entry, HttpContext.RequestAborted);
        return ok ? Ok(new { restarted = true, note }) : StatusCode(502, new { error = note ?? "restart failed" });
    }


    [HttpGet("{id}/live")]
    [EnableRateLimiting("fetch")]
    public async Task<IActionResult> Live(string id, CancellationToken ct) {
        if (await ResolveDeviceIdAsync(id, ct) is not { } realId) return Ok(new { found = false });
        if (services.GetService(typeof(DeviceCaptureManager)) is not DeviceCaptureManager mgr)
            return Ok(new { found = false });
        bool isAdmin = currentUser.IsAtLeast(UserRole.Admin);
        var d = mgr.DiagFor(realId);
        object capture = new {
            listening = mgr.PortFor(realId) != 0,
            port = isAdmin ? mgr.PortFor(realId) : 0,
            clientConnects = d.ClientConnects,
            auxbrainConnects = d.AuxbrainConnects,
            flows = d.Flows,
            rinfoHarvests = d.RinfoHarvests,
            lastDecryptError = isAdmin ? d.LastDecryptError : null,
            recentConnects = isAdmin ? d.RecentConnects : null
        };
        var v = mgr.Rinfo.Latest(realId);
        if (v is null) return Ok(new { found = false, capture });
        return isAdmin
            ? Ok(new { found = true, v.DeviceId, v.Platform, v.Version, v.Build, v.ClientVersion, v.LastSeen, capture })
            : Ok(new { found = true, v.Platform, v.Version, v.Build, v.ClientVersion, capture });
    }

}
