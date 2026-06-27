using EggIncognito.Core.Services.Devices;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.Services.Devices;
using EggIncognito.Services.ProtoExtract;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Services;

// Resolves a mesh (.rpo/.rpoz) stem to a decoded .glb by pulling it off a connected device and caching the
// result, so no game asset is ever shipped in the repo. Shared by the device-mesh route and the playground
// environment: both want "give me stem X as glb", cache-first, pulled live from a device bundle. Game assets
// stay the property of Auxbrain; this only reformats what a device already has.
//
// Asset-source device: an explicit id, else the first reachable enabled device (env meshes have no device of
// their own, so they borrow whichever device is online). Cache key = (platform, stem), shared with the
// device-mesh cache so a mesh pulled by either path is reused by the other.
public sealed class DeviceMeshProvider(
    IServiceProvider services, MeshAssetCache cache, IProcessRunner runner,
    IConfiguration config, ILogger<DeviceMeshProvider> logger)
{
    private const string PlatformAndroid = "android";
    private const string PlatformIos = "ios";

    public sealed record Result(bool Ok, byte[]? Glb, string? Diagnostics, int Status);

    private static Result Err(int status, string diag) => new(false, null, diag, status);

    private IDeviceStatusStore? Store => services.GetService(typeof(IDeviceStatusStore)) as IDeviceStatusStore;
    private EggIncognitoDbContext? Db => services.GetService(typeof(EggIncognitoDbContext)) as EggIncognitoDbContext;

    // The decoded glb for a stem. Lookup order: Postgres cache (durable, shared) -> on-disk cache (fast local
    // mirror) -> pull off the device + decode (only for a stem not yet stored anywhere). A device pull writes
    // through to BOTH caches so the next request (any instance) skips the device. deviceId null = first
    // reachable enabled device. Stem must be a bare file stem (no path separators / traversal).
    public async Task<Result> GetGlbAsync(string stem, string? deviceId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(stem) || stem.IndexOfAny(['/', '\\', '.']) >= 0)
            return Err(400, "invalid mesh name");

        var device = await ResolveDeviceAsync(deviceId, ct);
        var platform = device?.Platform;

        // 1) Postgres cache. Try the resolved platform first; if no device is online, try any platform's copy
        // (a previously-cached mesh serves even with every device offline).
        if (await TryDbGetAsync(platform, stem, ct) is { } dbGlb)
        {
            if (cache.TryGet(platform ?? "db", stem) is null) await cache.PutAsync(platform ?? "db", stem, dbGlb, ct);
            return new Result(true, dbGlb, null, 200);
        }

        // 2) on-disk cache (only meaningful once we know the platform).
        if (platform is not null && cache.TryGet(platform, stem) is { } diskGlb)
        {
            await PutDbAsync(platform, stem, diskGlb, ct); // backfill the DB cache from a pre-DB on-disk entry
            return new Result(true, diskGlb, null, 200);
        }

        // 3) pull off the device.
        if (device is null) return Err(503, "mesh not cached and no asset-source device available");

        byte[]? rpo;
        if (device.Platform == PlatformIos)
        {
            if (IosSsh(device) is not { } ssh)
                return Err(503, "ios mesh pull needs DeviceUpdate:Ios:SshKeyPath configured");
            rpo = await new IosAssetPuller(runner, ssh.Host, ssh.Port, ssh.Key).PullOneRpoAsync(device.Package, stem, ct);
        }
        else if (device.Platform == PlatformAndroid)
        {
            var apk = await new DeviceApkPuller(runner).PullBaseSplitAsync(device.Target, device.Package, ct);
            if (apk is null) return Err(502, "could not pull base.apk from the device");
            rpo = RpoAssetLister.ReadStem(apk, stem);
        }
        else return Err(501, $"no mesh pull for platform {device.Platform}");

        if (rpo is null) return Err(404, "mesh not found on device");

        var decode = RpoMeshDecoder.Decode(rpo, stem);
        if (!decode.Ok) return Err(500, decode.Diagnostics);
        var glb = decode.Glb!;
        await cache.PutAsync(device.Platform, stem, glb, ct);
        await PutDbAsync(device.Platform, stem, glb, ct);
        logger.LogInformation("device mesh: pulled {Stem} off {Id} ({Plat}), cached to db + disk", stem, device.Id, device.Platform);
        return new Result(true, glb, null, 200);
    }

    // The cached glb for (platform, stem) from Postgres, or null (miss / no DB). When platform is null (no
    // device online) any platform's stored copy is accepted.
    private async Task<byte[]?> TryDbGetAsync(string? platform, string stem, CancellationToken ct)
    {
        var db = Db;
        if (db is null) return null;
        try
        {
            var q = db.StoredMeshes.AsNoTracking().Where(m => m.Stem == stem);
            if (platform is not null) q = q.Where(m => m.Platform == platform);
            var row = await q.OrderByDescending(m => m.CreatedAt).FirstOrDefaultAsync(ct);
            return row?.Glb;
        }
        catch (Exception ex) { logger.LogWarning(ex, "mesh db cache read failed {Stem}", stem); return null; }
    }

    // Upserts the glb into the Postgres cache (no-op without a DB). Idempotent on (platform, stem).
    private async Task PutDbAsync(string platform, string stem, byte[] glb, CancellationToken ct)
    {
        var db = Db;
        if (db is null) return;
        try
        {
            var existing = await db.StoredMeshes.FirstOrDefaultAsync(m => m.Platform == platform && m.Stem == stem, ct);
            if (existing is null)
                db.StoredMeshes.Add(new StoredMesh { Platform = platform, Stem = stem, Glb = glb, ByteSize = glb.Length });
            else { existing.Glb = glb; existing.ByteSize = glb.Length; }
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) { logger.LogWarning(ex, "mesh db cache write failed {Stem}", stem); }
    }

    // The named device, or (when null) the first reachable enabled device. Reachability comes from the latest
    // probe row; falls back to the first enabled device if no probe data exists yet.
    private async Task<Device?> ResolveDeviceAsync(string? deviceId, CancellationToken ct)
    {
        var store = Store;
        if (store is null) return null;
        if (!string.IsNullOrEmpty(deviceId)) return await store.GetAsync(deviceId, ct);

        var devices = await store.EnabledDevicesAsync(ct);
        if (devices.Count == 0) return null;
        var latest = (await store.LatestPerDeviceAsync(ct)).ToDictionary(p => p.DeviceId);
        var reachable = devices.FirstOrDefault(d => latest.TryGetValue(d.Id, out var p) && p.Reachable);
        return reachable ?? devices[0];
    }

    private (string Host, string Port, string Key)? IosSsh(Device device)
    {
        var cfg = config.GetSection("DeviceUpdate").GetSection("Ios");
        var key = cfg["SshKeyPath"];
        if (string.IsNullOrEmpty(key)) return null;
        var host = string.IsNullOrEmpty(cfg["SshHost"]) ? device.Target : cfg["SshHost"]!;
        return (host, cfg["SshPort"] ?? "2222", key);
    }
}
