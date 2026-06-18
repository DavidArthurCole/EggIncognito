using EggIncognito.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Data.Services;

// Persists the device roster + append-only probe history. Latest-per-device drives the /protos indicator;
// history backs the activity log. Keyed on the config-supplied device id. DB-gated like every Data store.
public interface IDeviceStatusStore
{
    Task UpsertDeviceAsync(string id, string platform, string label, string target, string package, CancellationToken ct = default);
    Task<List<Device>> EnabledDevicesAsync(CancellationToken ct = default);
    Task<Device?> GetAsync(string id, CancellationToken ct = default);
    Task RecordProbeAsync(DeviceProbe row, CancellationToken ct = default);
    Task<List<DeviceProbe>> LatestPerDeviceAsync(CancellationToken ct = default);
    Task<List<DeviceProbe>> HistoryAsync(string deviceId, int n, CancellationToken ct = default);
    Task RecordUpdateAsync(DeviceUpdate row, CancellationToken ct = default);
    Task<List<DeviceUpdate>> LatestUpdatePerDeviceAsync(CancellationToken ct = default);
    Task<List<DeviceUpdate>> UpdateHistoryAsync(string deviceId, int n, CancellationToken ct = default);
}

public sealed class DeviceStatusStore(EggIncognitoDbContext db) : IDeviceStatusStore
{
    public async Task UpsertDeviceAsync(string id, string platform, string label, string target, string package, CancellationToken ct = default)
    {
        var row = await db.Devices.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (row is null)
        {
            db.Devices.Add(new Device { Id = id, Platform = platform, Label = label, Target = target, Package = package, Enabled = true });
        }
        else
        {
            row.Platform = platform; row.Label = label; row.Target = target; row.Package = package; row.Enabled = true;
        }
        await db.SaveChangesAsync(ct);
    }

    public Task<List<Device>> EnabledDevicesAsync(CancellationToken ct = default) =>
        db.Devices.AsNoTracking().Where(d => d.Enabled).OrderBy(d => d.Id).ToListAsync(ct);

    public Task<Device?> GetAsync(string id, CancellationToken ct = default) =>
        db.Devices.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct);

    public async Task RecordProbeAsync(DeviceProbe row, CancellationToken ct = default)
    {
        db.DeviceProbes.Add(row);
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<DeviceProbe>> LatestPerDeviceAsync(CancellationToken ct = default)
    {
        // Per device, the row with the greatest probed_at. Correlated subquery so Postgres uses the
        // (device_id, probed_at) index instead of scanning the whole append-only history into memory.
        return await db.DeviceProbes.AsNoTracking()
            .Where(p => p.ProbedAt == db.DeviceProbes
                .Where(x => x.DeviceId == p.DeviceId)
                .Max(x => x.ProbedAt))
            .ToListAsync(ct);
    }

    public Task<List<DeviceProbe>> HistoryAsync(string deviceId, int n, CancellationToken ct = default) =>
        db.DeviceProbes.AsNoTracking().Where(p => p.DeviceId == deviceId)
            .OrderByDescending(p => p.ProbedAt).Take(n).ToListAsync(ct);

    public async Task RecordUpdateAsync(DeviceUpdate row, CancellationToken ct = default)
    {
        db.DeviceUpdates.Add(row);
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<DeviceUpdate>> LatestUpdatePerDeviceAsync(CancellationToken ct = default)
    {
        return await db.DeviceUpdates.AsNoTracking()
            .Where(u => u.AttemptedAt == db.DeviceUpdates
                .Where(x => x.DeviceId == u.DeviceId)
                .Max(x => x.AttemptedAt))
            .ToListAsync(ct);
    }

    public Task<List<DeviceUpdate>> UpdateHistoryAsync(string deviceId, int n, CancellationToken ct = default) =>
        db.DeviceUpdates.AsNoTracking().Where(u => u.DeviceId == deviceId)
            .OrderByDescending(u => u.AttemptedAt).Take(n).ToListAsync(ct);
}
