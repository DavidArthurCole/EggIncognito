using System.Security.Cryptography;
using System.Text;
using EggIdentity.Contract;
using EggIncognito.Core.Services.Devices;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.Services;
using EggIncognito.Services.Assets;
using EggIncognito.Services.Auth;
using EggIncognito.Services.Devices;
using EggIncognito.Services.Feed;
using EggIncognito.Services.ProtoExtract;
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
    IServiceScopeFactory scopeFactory,
    IDeviceJobTracker jobs) : ControllerBase {
    private const string PlatformAndroid = "android";
    private const string PlatformIos = "ios";
    private IDeviceStatusStore? Store => services.GetService(typeof(IDeviceStatusStore)) as IDeviceStatusStore;
    private EggIncognitoDbContext? Db => services.GetService(typeof(EggIncognitoDbContext)) as EggIncognitoDbContext;

    private ObjectResult? RequireAdmin() =>
        currentUser.IsAtLeast(UserRole.Admin) ? null : StatusCode(403, new { error = "admin role required" });

    private static string DeviceKey(string realId) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(realId)))[..16];

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
        if (store is null) return Ok(Array.Empty<object>());
        var latest = await store.LatestPerDeviceAsync();
        var devices = (await store.EnabledDevicesAsync()).ToDictionary(d => d.Id);
        var updates = (await store.LatestUpdatePerDeviceAsync()).ToDictionary(u => u.DeviceId);


        var db = Db;
        var storeLatest = new Dictionary<string, string?>();


        var regLatestApp = new Dictionary<string, string?>();
        var regLatestBuild = new Dictionary<string, string?>();
        if (db is not null) {
            foreach (string plat in devices.Values.Select(d => d.Platform).Distinct()) {
                storeLatest[plat] = await StoreAheadCheck.StoreLatestAsync(db, plat, HttpContext.RequestAborted);


                var extracted = await db.ProtoVersions.AsNoTracking()
                    .Where(v => v.Platform == plat && (v.DeletedAt == null || v.CanonicalId != null))
                    .Select(v => new { v.Build, v.AppVersion })
                    .ToListAsync(HttpContext.RequestAborted);
                regLatestApp[plat] = extracted.Select(e => e.AppVersion)
                    .OrderByDescending(v => v, Comparer<string>.Create(DeviceProbeRunner.SemverCompare))
                    .FirstOrDefault();
                regLatestBuild[plat] = plat == "android"
                    ? extracted.Select(e => e.Build).Where(b => long.TryParse(b, out _)).OrderByDescending(long.Parse)
                        .FirstOrDefault()
                    : null;
            }
        }

        bool isAdmin = currentUser.IsAtLeast(UserRole.Admin);
        var rows = latest.Where(p => devices.ContainsKey(p.DeviceId)).Select(p => {
            var d = devices[p.DeviceId];
            updates.TryGetValue(d.Id, out var up);
            string? sl = storeLatest.GetValueOrDefault(d.Platform);

            string liveResult = p.Reachable && !string.IsNullOrEmpty(p.InstalledAppVersion)
                ? DeviceProbeRunner.Classify(
                    new DeviceProbeResult(true, p.InstalledAppVersion, p.InstalledBuild, null),
                    d.Platform, regLatestBuild.GetValueOrDefault(d.Platform),
                    regLatestApp.GetValueOrDefault(d.Platform))
                : p.Result;
            return new {
                id = isAdmin ? d.Id : DeviceKey(d.Id),
                platform = d.Platform,
                label = d.Label,
                reachable = p.Reachable,
                installedAppVersion = p.InstalledAppVersion,
                installedBuild = p.InstalledBuild,
                latestAvailable = p.LatestAvailable,
                storeLatest = sl,
                storeAhead = StoreAheadCheck.IsAhead(sl, p.InstalledAppVersion),
                result = liveResult,
                note = isAdmin ? p.Note : null,
                probedAt = p.ProbedAt,
                lastUpdate = !isAdmin || up is null
                    ? null
                    : new {
                        status = up.Status,
                        from = up.FromVersion,
                        to = up.ToVersion,
                        note = up.Note,
                        by = up.TriggeredBy,
                        at = up.AttemptedAt
                    }
            };
        });
        return Ok(rows);
    }

    [HttpGet("{id}/history")]
    public async Task<IActionResult> History(string id, [FromQuery] int n = 20) {
        if (RequireAdmin() is { } no) return no;
        var store = Store;
        if (store is null) return Ok(Array.Empty<object>());
        var rows = await store.HistoryAsync(id, Math.Clamp(n, 1, 100));
        return Ok(rows.Select(p => new {
            probedAt = p.ProbedAt,
            reachable = p.Reachable,
            installedAppVersion = p.InstalledAppVersion,
            installedBuild = p.InstalledBuild,
            result = p.Result,
            triggeredBy = p.TriggeredBy,
            note = p.Note
        }));
    }

    [HttpPost("{id}/refresh")]
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
        if (store is null || db is null) return StatusCode(503, new { error = "no database configured" });

        var device = await store.GetAsync(id);
        if (device is null) return NotFound(new { error = "unknown device" });

        var runner = (IProcessRunner)services.GetRequiredService(typeof(IProcessRunner));
        var time = (TimeProvider)services.GetRequiredService(typeof(TimeProvider));
        var logger = (ILogger<DevicesController>)services.GetRequiredService(typeof(ILogger<DevicesController>));

        var row = await DeviceProbeRunner.ProbeOneAsync(
            device, $"admin:{currentUser.DiscordId}", runner, store, db, logger, time, HttpContext.RequestAborted);

        return Ok(new {
            id = device.Id,
            platform = device.Platform,
            label = device.Label,
            reachable = row.Reachable,
            installedAppVersion = row.InstalledAppVersion,
            installedBuild = row.InstalledBuild,
            latestAvailable = row.LatestAvailable,
            result = row.Result,
            note = row.Note,
            probedAt = row.ProbedAt
        });
    }


    [HttpPost("refresh-all")]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> RefreshAll() {
        if (RequireAdmin() is { } no) return no;

        if (services.GetService(typeof(IDeviceAgentClient)) is IDeviceAgentClient agent && agent.Enabled) {
            int probedByAgent = await agent.ProbeAllAsync(HttpContext.RequestAborted);
            return Ok(new { probed = probedByAgent });
        }

        var store = Store;
        var db = Db;
        if (store is null || db is null) return StatusCode(503, new { error = "no database configured" });

        var runner = (IProcessRunner)services.GetRequiredService(typeof(IProcessRunner));
        var time = (TimeProvider)services.GetRequiredService(typeof(TimeProvider));
        var logger = (ILogger<DevicesController>)services.GetRequiredService(typeof(ILogger<DevicesController>));

        var devices = await store.EnabledDevicesAsync();
        int n = 0;
        foreach (var d in devices) {
            try {
                await DeviceProbeRunner.ProbeOneAsync(
                    d, $"admin-all:{currentUser.DiscordId}", runner, store, db, logger, time,
                    HttpContext.RequestAborted);
                n++;
            } catch (Exception ex) {
                logger.LogWarning(ex, "refresh-all: {Id} threw", d.Id);
            }
        }

        return Ok(new { probed = n });
    }


    [HttpPost("{id}/check-update")]
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


        if (!jobs.TryStart(id, "checking store..."))
            return StatusCode(409, new { error = "check already running" });

        logger.LogInformation("device check-update: {Id} start (by {Who})", id, who);
        var target = new DeviceTarget(device.Id, device.Platform, device.Target, device.Package);


        _ = Task.Run(() => RunCheckUpdateAsync(id, target, checker, who));

        return Accepted(new { id = device.Id, action = "running" });
    }


    private async Task RunCheckUpdateAsync(string id, DeviceTarget target, IDeviceStoreChecker checker,
        string who) {
        using var scope = scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var logger = sp.GetRequiredService<ILogger<DevicesController>>();
        try {
            var store = sp.GetService<IDeviceStatusStore>();
            var db = sp.GetService<EggIncognitoDbContext>();
            var runner = sp.GetRequiredService<IProcessRunner>();
            if (store is null || db is null) {
                jobs.Fail(id, "no database configured");
                return;
            }


            var result =
                await checker.CheckAndUpdateAsync(target, CancellationToken.None, msg => jobs.Progress(id, msg));

            if (result.Installed) {
                await store.RecordUpdateAsync(new DeviceUpdate {
                    DeviceId = id,
                    AttemptedAt = DateTimeOffset.UtcNow,
                    FromVersion = result.InstalledBefore,
                    ToVersion = result.InstalledAfter,
                    Status = "verified",
                    Note = result.Note,
                    TriggeredBy = $"check:{who}"
                }, CancellationToken.None);
            }

            var device = await store.GetAsync(id);
            if (device is not null) {
                var time = sp.GetRequiredService<TimeProvider>();
                await DeviceProbeRunner.ProbeOneAsync(
                    device, $"check-update:{who}", runner, store, db, logger, time, CancellationToken.None);
            }

            jobs.Finish(id, result);
        } catch (Exception ex) {
            logger.LogError(ex, "device check-update: {Id} background run failed", id);
            jobs.Fail(id, ex.Message);
        }
    }


    [HttpGet("{id}/check-status")]
    public IActionResult CheckStatus(string id) {
        if (RequireAdmin() is { } no) return no;
        var s = jobs.Get(id);
        if (s is null) return Ok(new { state = "idle" });
        return Ok(new {
            state = s.State.ToString().ToLowerInvariant(),
            message = s.Message,
            action = s.Action,
            installedBefore = s.InstalledBefore,
            installedAfter = s.InstalledAfter,
            startedAt = s.StartedAt,
            updatedAt = s.UpdatedAt
        });
    }


    [HttpPost("{id}/save")]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> Save(string id) {
        if (RequireAdmin() is { } no) return no;
        var store = Store;
        if (store is null || Db is null ||
            services.GetService(typeof(ProtoRegistryStore)) is not ProtoRegistryStore registry) {
            return StatusCode(503, new { error = "no database configured" });
        }

        var logger = (ILogger<DevicesController>)services.GetRequiredService(typeof(ILogger<DevicesController>));
        string who = currentUser.DiscordId ?? "?";

        var device = await store.GetAsync(id);
        if (device is null) return NotFound(new { error = "unknown device" });
        if (device.Platform is not (PlatformAndroid or PlatformIos))
            return StatusCode(501, new { error = $"no extractor for platform {device.Platform}" });

        logger.LogInformation("device save: {Id} start (by {Who})", id, who);

        var runner = (IProcessRunner)services.GetRequiredService(typeof(IProcessRunner));
        var probe = await DeviceProbeRunner.ProbeFor(device, runner).ProbeAsync(HttpContext.RequestAborted);

        bool needBuild = device.Platform == PlatformAndroid;
        if (!probe.Reachable || string.IsNullOrEmpty(probe.InstalledAppVersion) ||
            (needBuild && string.IsNullOrEmpty(probe.InstalledBuild))) {
            logger.LogWarning("device save: {Id} aborted: unreachable or no version ({Note})", id, probe.Note);
            return StatusCode(502, new { error = $"device unreachable or no version read: {probe.Note}" });
        }

        var (carve, err) = await PullAndCarveAsync(device, probe, runner, logger);
        if (err is not null) return err;

        string appVersion = probe.InstalledAppVersion!;
        string build = carve!.Build;
        string sha = Convert.ToHexStringLower(SHA256.HashData(
            Encoding.UTF8.GetBytes(carve.Proto)));


        string? clientVersion = carve.ClientVersion?.ToString()
                                ?? await HarvestClientVersionAsync(device, HttpContext.RequestAborted);
        logger.LogInformation("device save: {Id} clientVersion={Cv} (source={Src})", id, clientVersion ?? "(none)",
            carve.ClientVersion is not null ? "binary" : "harvest");

        try {
            (var row, bool created, bool protoChanged) = await registry.UpsertAsync(
                device.Platform, appVersion, build, clientVersion, device.Package,
                sha, $"device:{device.Id}", DateTimeOffset.UtcNow,
                $"device-save:{who}", carve.Proto, "device",
                true, HttpContext.RequestAborted);
            logger.LogInformation("device save: {Id} -> registry {Plat} build {Build} ({State}, sha {Sha})",
                id, device.Platform, build, created ? "created" : "updated", sha[..12]);


            var dispatcher = services.GetService(typeof(FeedDispatcher))
                as FeedDispatcher;
            if (dispatcher is not null) {
                var cfg = services.GetService(typeof(IConfiguration)) as IConfiguration;
                string pageUrl = FeedDispatcher.BuildPageUrl(
                    cfg?["Feed:PageBaseUrl"], device.Platform, build);
                await dispatcher.DispatchAsync(new ProtoBuildEvent(
                    row.Id, device.Platform, appVersion, build, null,
                    sha, created, protoChanged, pageUrl), HttpContext.RequestAborted);
            }
        } catch (Exception ex) {
            logger.LogError(ex, "device save: {Id} registry upsert failed for build {Build}", id, build);
            return StatusCode(500, new { error = $"registry write failed: {ex.Message}" });
        }

        var db = Db!;
        var time = (TimeProvider)services.GetRequiredService(typeof(TimeProvider));
        var reprobe = await DeviceProbeRunner.ProbeOneAsync(
            device, $"admin-save:{who}", runner, store, db, logger, time, HttpContext.RequestAborted);

        return Ok(new { saved = true, appVersion, build, result = reprobe.Result });
    }


    private async Task<(CarveResult? carve, IActionResult? err)> PullAndCarveAsync(
        Device device, DeviceProbeResult probe, IProcessRunner runner, ILogger logger) {
        if (device.Platform == PlatformAndroid) {
            byte[]? apk =
                await new DeviceApkPuller(runner).PullArmSplitAsync(device.Target, device.Package,
                    HttpContext.RequestAborted);
            if (apk is null) {
                logger.LogWarning("device save: {Id} aborted: arm split pull failed", device.Id);
                return (null, StatusCode(502, new { error = "could not pull the arm split apk from the device" }));
            }

            logger.LogInformation("device save: {Id} pulled arm split ({Bytes} bytes), carving proto", device.Id,
                apk.Length);
            var carved = ArchiveProtoExtractor.Extract(apk);
            if (!carved.Ok || string.IsNullOrEmpty(carved.Proto)) {
                logger.LogWarning("device save: {Id} carve failed: {Diag}", device.Id, carved.Diagnostics);
                return (null, StatusCode(500, new { error = $"proto carve failed: {carved.Diagnostics}" }));
            }

            return (new CarveResult(carved.Proto, probe.InstalledBuild!, carved.ClientVersion), null);
        }

        if (IosConn(device) is not { } conn) {
            logger.LogWarning("device save: {Id} aborted: ios ssh key not configured (DeviceUpdate:Ios:SshKeyPath)",
                device.Id);
            return (null,
                StatusCode(503, new { error = "ios extraction needs DeviceUpdate:Ios:SshKeyPath configured" }));
        }

        byte[]? bin = await new IosBinaryPuller(conn).PullBinaryAsync(device.Package, HttpContext.RequestAborted);
        if (bin is null) {
            logger.LogWarning("device save: {Id} aborted: ios binary pull failed", device.Id);
            return (null, StatusCode(502, new { error = "could not pull the egginc binary from the device over ssh" }));
        }

        logger.LogInformation("device save: {Id} pulled ios binary ({Bytes} bytes), carving proto", device.Id,
            bin.Length);


        string? stashPath =
            (services.GetService(typeof(IConfiguration)) as IConfiguration)?["Runner:IosBinaryStashPath"];
        if (!string.IsNullOrEmpty(stashPath)) {
            try {
                await System.IO.File.WriteAllBytesAsync(stashPath, bin, HttpContext.RequestAborted);
            } catch (Exception ex) {
                logger.LogWarning(ex, "device save: {Id} could not stash ios binary to {Path}", device.Id, stashPath);
            }
        }

        var iosCarve = MachoProtoExtractor.Extract(bin);
        if (!iosCarve.Ok || string.IsNullOrEmpty(iosCarve.Proto)) {
            logger.LogWarning("device save: {Id} carve failed: {Diag}", device.Id, iosCarve.Diagnostics);
            return (null, StatusCode(500, new { error = $"proto carve failed: {iosCarve.Diagnostics}" }));
        }


        string iosBuild = !string.IsNullOrEmpty(probe.InstalledBuild)
            ? probe.InstalledBuild!
            : Convert.ToHexStringLower(SHA256.HashData(bin))[..16];
        return (new CarveResult(iosCarve.Proto, iosBuild, LibegincClientVersion.ReadFromBinary(bin)), null);
    }


    private async Task<string?> HarvestClientVersionAsync(Device device, CancellationToken ct) {
        if (services.GetService(typeof(DeviceProxyPusher)) is not DeviceProxyPusher pusher ||
            services.GetService(typeof(DeviceConfig)) is not DeviceConfig devCfg) {
            return null;
        }

        var entry = devCfg.Devices.FirstOrDefault(d => d.Id == device.Id);
        if (entry is null) return null;

        var rinfo = await pusher.ForceHarvestAsync(entry, TimeSpan.FromSeconds(5), ct);
        return rinfo?.ClientVersion?.ToString();
    }


    private SshDeviceConnection? IosConn(Device device) =>
        ((IDeviceConnectionFactory)services.GetRequiredService(typeof(IDeviceConnectionFactory))).Ios(device.Target);


    [HttpPost("{id}/pull-meshes")]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> PullMeshes(string id, [FromQuery] bool export = false,
        [FromQuery] string? build = null) {
        if (RequireAdmin() is { } no) return no;
        var store = Store;
        if (store is null) return StatusCode(503, new { error = "no database configured" });

        var device = await store.GetAsync(id);
        if (device is null) return NotFound(new { error = "unknown device" });
        if (device.Platform is not (PlatformAndroid or PlatformIos))
            return StatusCode(501, new { error = $"no mesh puller for platform {device.Platform}" });

        var runner = (IProcessRunner)services.GetRequiredService(typeof(IProcessRunner));
        var ct = HttpContext.RequestAborted;

        RpoAssetExtractor.ExtractResult extract;
        if (device.Platform == PlatformAndroid) {
            byte[]? apk = await new DeviceApkPuller(runner).PullBaseSplitAsync(device.Target, device.Package, ct);
            if (apk is null) return StatusCode(502, new { error = "could not pull base.apk from the device" });
            extract = RpoAssetExtractor.Extract(apk);
        } else {
            if (IosConn(device) is not { } conn)
                return StatusCode(503, new { error = "ios mesh pull needs DeviceUpdate:Ios:SshKeyPath configured" });
            byte[]? tar = await new IosAssetPuller(conn).PullRposTarAsync(device.Package, ct);
            if (tar is null)
                return StatusCode(502, new { error = "could not pull the rpos meshes from the device over ssh" });
            var entries = TarReader.Read(tar)
                .Select(e => (e.Name, e.Bytes));
            extract = RpoAssetExtractor.FromEntries(entries);
        }


        return Ok(export ? MeshManifest.Ships(extract, build, false, null) : MeshManifest.From(extract));
    }


    [HttpGet("{id}/list-meshes")]
    [EnableRateLimiting("read")]
    public async Task<IActionResult> ListMeshes(string id) {
        if (RequireAdmin() is { } no) return no;
        var store = Store;
        if (store is null) return StatusCode(503, new { error = "no database configured" });
        var device = await store.GetAsync(id);
        if (device is null) return NotFound(new { error = "unknown device" });

        var runner = (IProcessRunner)services.GetRequiredService(typeof(IProcessRunner));
        var ct = HttpContext.RequestAborted;

        if (device.Platform == PlatformIos) {
            if (IosConn(device) is not { } conn)
                return StatusCode(503, new { error = "ios mesh listing needs DeviceUpdate:Ios:SshKeyPath configured" });
            var names = await new IosAssetPuller(conn).ListRposAsync(device.Package, ct);
            return Ok(new { meshes = names });
        }

        if (device.Platform == PlatformAndroid) {
            byte[]? apk = await new DeviceApkPuller(runner).PullBaseSplitAsync(device.Target, device.Package, ct);
            if (apk is null) return StatusCode(502, new { error = "could not pull base.apk from the device" });
            var names = RpoAssetLister.ListStems(apk);
            return Ok(new { meshes = names });
        }

        return StatusCode(501, new { error = $"no mesh listing for platform {device.Platform}" });
    }


    [HttpGet("{id}/mesh/{stem}")]
    [EnableRateLimiting("read")]
    public async Task<IActionResult> Mesh(string id, string stem, [FromQuery] string? animate,
        [FromQuery] float seconds) {
        if (RequireAdmin() is { } no) return no;
        var ct = HttpContext.RequestAborted;


        var provider = (DeviceMeshProvider)services.GetRequiredService(typeof(DeviceMeshProvider));
        var res = await provider.GetGlbAsync(stem, id, ct);
        if (!res.Ok) return StatusCode(res.Status, new { error = res.Diagnostics });
        byte[] glb = res.Glb!;

        if (!string.IsNullOrEmpty(animate)) {
            var opts = new GltfAnimator.Options(
                GltfAnimator.ParseKind(animate), seconds > 0 ? seconds : 6f);
            var anim = GltfAnimator.Animate(glb, opts);
            if (anim.Ok) glb = anim.Glb!;
        }

        return File(glb, "model/gltf-binary", $"{stem}.glb");
    }


    [HttpPost("{id}/precache-meshes")]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> PrecacheMeshes(string id) {
        if (RequireAdmin() is { } no) return no;
        var store = Store;
        if (store is null) return StatusCode(503, new { error = "no database configured" });
        var device = await store.GetAsync(id);
        if (device is null) return NotFound(new { error = "unknown device" });

        if (services.GetService(typeof(MeshAssetCache)) is not MeshAssetCache cache || !cache.Enabled)
            return StatusCode(503, new { error = "mesh cache needs ShipAssets:OutputDir configured" });

        var runner = (IProcessRunner)services.GetRequiredService(typeof(IProcessRunner));
        var ct = HttpContext.RequestAborted;

        RpoAssetExtractor.ExtractResult extract;
        if (device.Platform == PlatformAndroid) {
            byte[]? apk = await new DeviceApkPuller(runner).PullBaseSplitAsync(device.Target, device.Package, ct);
            if (apk is null) return StatusCode(502, new { error = "could not pull base.apk from the device" });
            extract = RpoAssetExtractor.Extract(apk);
        } else if (device.Platform == PlatformIos) {
            if (IosConn(device) is not { } conn)
                return StatusCode(503, new { error = "ios mesh pull needs DeviceUpdate:Ios:SshKeyPath configured" });
            byte[]? tar = await new IosAssetPuller(conn).PullRposTarAsync(device.Package, ct);
            if (tar is null) return StatusCode(502, new { error = "could not pull the rpos meshes over ssh" });
            extract = RpoAssetExtractor.FromEntries(
                TarReader.Read(tar).Select(e => (e.Name, e.Bytes)));
        } else {
            return StatusCode(501, new { error = $"no mesh pull for platform {device.Platform}" });
        }

        int cached = 0;
        var failed = new List<string>();
        foreach (var asset in extract.Assets) {
            if (asset.Decode.Ok && asset.Decode.Glb is { } g) {
                await cache.PutAsync(device.Platform, asset.Key, g, ct);
                cached++;
            } else {
                failed.Add(asset.Key);
            }
        }

        return Ok(new { ok = true, platform = device.Platform, cached, failed = failed.Count, failedKeys = failed.Take(20) });
    }


    [HttpGet("{id}/cached-meshes")]
    [EnableRateLimiting("read")]
    public async Task<IActionResult> CachedMeshes(string id) {
        if (RequireAdmin() is { } no) return no;
        var device = await Store?.GetAsync(id)!;
        if (device is null) return NotFound(new { error = "unknown device" });
        if (services.GetService(typeof(MeshAssetCache)) is not MeshAssetCache cache || !cache.Enabled)
            return Ok(new { enabled = false, meshes = Array.Empty<object>() });
        var meshes = cache.List(device.Platform)
            .Select(m => new { stem = m.Stem, bytes = m.Bytes, cachedAt = m.CachedAt });
        return Ok(new { enabled = true, platform = device.Platform, meshes });
    }


    [HttpDelete("{id}/cached-meshes/{stem}")]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> DeleteCachedMesh(string id, string stem) {
        if (RequireAdmin() is { } no) return no;
        var device = await Store?.GetAsync(id)!;
        if (device is null) return NotFound(new { error = "unknown device" });
        if (services.GetService(typeof(MeshAssetCache)) is not MeshAssetCache cache || !cache.Enabled)
            return StatusCode(503, new { error = "mesh cache not configured" });

        if (stem == "*") {
            int n = cache.Clear(device.Platform);
            return Ok(new { ok = true, cleared = n });
        }

        bool deleted = cache.Delete(device.Platform, stem);
        return Ok(new { ok = deleted, deleted });
    }


    [HttpPost("{id}/restart-app")]
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

    private sealed record CarveResult(string Proto, string Build, int? ClientVersion = null);
}
