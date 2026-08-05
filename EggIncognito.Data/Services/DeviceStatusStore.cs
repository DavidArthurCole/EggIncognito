using EggIncognito.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Data.Services;

public interface IDeviceStatusStore {
    Task UpsertDeviceAsync(string id, string platform, string label, string target, string package,
        CancellationToken ct = default);

    Task<List<Device>> EnabledDevicesAsync(CancellationToken ct = default);
    Task<Device?> GetAsync(string id, CancellationToken ct = default);
    Task RecordProbeAsync(DeviceProbe row, CancellationToken ct = default);
    Task<List<DeviceProbe>> LatestPerDeviceAsync(CancellationToken ct = default);
    Task<List<DeviceProbe>> HistoryAsync(string deviceId, int n, CancellationToken ct = default);
    Task RecordUpdateAsync(DeviceUpdate row, CancellationToken ct = default);
    Task<List<DeviceUpdate>> LatestUpdatePerDeviceAsync(CancellationToken ct = default);
    Task<List<DeviceUpdate>> UpdateHistoryAsync(string deviceId, int n, CancellationToken ct = default);
    Task<List<DeviceProbeStats>> ProbeStatsAsync(TimeSpan window, CancellationToken ct = default);
}

public sealed record DeviceProbeStats(
    string DeviceId,
    int Total,
    int ReachableCount,
    DateTimeOffset? LastSuccessAt,
    DateTimeOffset? LastFailureAt,
    int ConsecutiveFailures,
    IReadOnlyDictionary<string, int> ResultCounts);

public sealed class DeviceStatusStore(EggIncognitoDbContext db) : IDeviceStatusStore {
    public async Task UpsertDeviceAsync(string id, string platform, string label, string target, string package,
        CancellationToken ct = default) {
        var row = await db.Devices.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (row is null) {
            db.Devices.Add(new Device { Id = id, Platform = platform, Label = label, Target = target, Package = package, Enabled = true });
        } else {
            row.Platform = platform;
            row.Label = label;
            row.Target = target;
            row.Package = package;
            row.Enabled = true;
        }

        await db.SaveChangesAsync(ct);
    }

    public Task<List<Device>> EnabledDevicesAsync(CancellationToken ct = default) =>
        db.Devices.AsNoTracking().Where(d => d.Enabled).OrderBy(d => d.Id).ToListAsync(ct);

    public Task<Device?> GetAsync(string id, CancellationToken ct = default) =>
        db.Devices.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct);

    public async Task RecordProbeAsync(DeviceProbe row, CancellationToken ct = default) {
        db.DeviceProbes.Add(row);
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<DeviceProbe>> LatestPerDeviceAsync(CancellationToken ct = default) {
        return await db.DeviceProbes.AsNoTracking()
            .Where(p => p.ProbedAt == db.DeviceProbes
                .Where(x => x.DeviceId == p.DeviceId)
                .Max(x => x.ProbedAt))
            .ToListAsync(ct);
    }

    public Task<List<DeviceProbe>> HistoryAsync(string deviceId, int n, CancellationToken ct = default) =>
        db.DeviceProbes.AsNoTracking().Where(p => p.DeviceId == deviceId)
            .OrderByDescending(p => p.ProbedAt).Take(n).ToListAsync(ct);

    public async Task RecordUpdateAsync(DeviceUpdate row, CancellationToken ct = default) {
        db.DeviceUpdates.Add(row);
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<DeviceUpdate>> LatestUpdatePerDeviceAsync(CancellationToken ct = default) {
        return await db.DeviceUpdates.AsNoTracking()
            .Where(u => u.AttemptedAt == db.DeviceUpdates
                .Where(x => x.DeviceId == u.DeviceId)
                .Max(x => x.AttemptedAt))
            .ToListAsync(ct);
    }

    public Task<List<DeviceUpdate>> UpdateHistoryAsync(string deviceId, int n, CancellationToken ct = default) =>
        db.DeviceUpdates.AsNoTracking().Where(u => u.DeviceId == deviceId)
            .OrderByDescending(u => u.AttemptedAt).Take(n).ToListAsync(ct);

    public async Task<List<DeviceProbeStats>> ProbeStatsAsync(TimeSpan window, CancellationToken ct = default) {
        var enabledIds = await db.Devices.AsNoTracking().Where(d => d.Enabled).Select(d => d.Id).ToListAsync(ct);
        if (enabledIds.Count == 0) return [];

        var cutoff = DateTimeOffset.UtcNow - window;

        var totals = await db.DeviceProbes.AsNoTracking()
            .Where(p => enabledIds.Contains(p.DeviceId) && p.ProbedAt >= cutoff)
            .GroupBy(p => p.DeviceId)
            .Select(g => new { DeviceId = g.Key, Total = g.Count(), Reachable = g.Count(x => x.Reachable) })
            .ToListAsync(ct);
        var totalsMap = totals.ToDictionary(x => x.DeviceId);

        var resultCounts = await db.DeviceProbes.AsNoTracking()
            .Where(p => enabledIds.Contains(p.DeviceId) && p.ProbedAt >= cutoff)
            .GroupBy(p => new { p.DeviceId, p.Result })
            .Select(g => new { g.Key.DeviceId, g.Key.Result, Count = g.Count() })
            .ToListAsync(ct);

        var lastSuccess = await db.DeviceProbes.AsNoTracking()
            .Where(p => enabledIds.Contains(p.DeviceId) && p.Reachable)
            .GroupBy(p => p.DeviceId)
            .Select(g => new { DeviceId = g.Key, At = g.Max(x => x.ProbedAt) })
            .ToListAsync(ct);
        var lastSuccessMap = lastSuccess.ToDictionary(x => x.DeviceId, x => x.At);

        var lastFailure = await db.DeviceProbes.AsNoTracking()
            .Where(p => enabledIds.Contains(p.DeviceId) && !p.Reachable)
            .GroupBy(p => p.DeviceId)
            .Select(g => new { DeviceId = g.Key, At = g.Max(x => x.ProbedAt) })
            .ToListAsync(ct);
        var lastFailureMap = lastFailure.ToDictionary(x => x.DeviceId, x => x.At);

        var consecutiveFailures = new Dictionary<string, int>();
        foreach (string id in enabledIds) {
            consecutiveFailures[id] = lastSuccessMap.TryGetValue(id, out var since)
                ? await db.DeviceProbes.AsNoTracking().Where(p => p.DeviceId == id && p.ProbedAt > since).CountAsync(ct)
                : await db.DeviceProbes.AsNoTracking().Where(p => p.DeviceId == id && !p.Reachable).CountAsync(ct);
        }

        return enabledIds.Select(id => new DeviceProbeStats(
            id,
            totalsMap.TryGetValue(id, out var t) ? t.Total : 0,
            totalsMap.TryGetValue(id, out var t2) ? t2.Reachable : 0,
            lastSuccessMap.TryGetValue(id, out var ls) ? ls : null,
            lastFailureMap.TryGetValue(id, out var lf) ? lf : null,
            consecutiveFailures.GetValueOrDefault(id),
            resultCounts.Where(r => r.DeviceId == id)
                .ToDictionary(r => r.Result, r => r.Count))).ToList();
    }
}
