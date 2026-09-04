using EggIncognito.Data.Services;
using EggIncognito.Models.Devices;

namespace EggIncognito.Services.Devices;

public sealed class DeviceJobFeed(IServiceScopeFactory scopes, DeviceTimelineCache cache) {
    public async Task<IReadOnlyList<LiveJob>> LiveAsync(CancellationToken ct) {
        using var scope = scopes.CreateScope();
        if (scope.ServiceProvider.GetService<IDeviceStatusStore>() is not { } store) return [];

        var ids = (await store.EnabledDevicesAsync(ct)).Select(d => d.Id).ToList();
        var running = await cache.RunningAsync(ids, ct);
        return [.. running.Select(j => new LiveJob(j.DeviceId, j.Id, j.Kind, j.Message, j.StartedAt))];
    }

    public async Task<JobPage> PageAsync(string deviceId, int take, long? before, CancellationToken ct) {
        int size = JobGroupCollapser.ClampTake(take);
        int batch = JobGroupCollapser.BatchFor(size);
        var collapser = new JobGroupCollapser(size);
        long? cursor = before;
        while (true) {
            var rows = await cache.PageAsync(deviceId, batch, cursor, ct);
            collapser.Feed(rows);
            if (collapser.Complete || rows.Count < batch) break;
            cursor = rows[^1].Id;
        }

        return collapser.Finish();
    }

    public async Task<IReadOnlyList<JobLineRow>> LinesAsync(string deviceId, long jobId, CancellationToken ct) {
        var lines = await cache.LinesAsync(deviceId, jobId, ct);
        return [.. lines.Select(l => new JobLineRow(l.At, l.Level, l.Text, l.Entry, l.Bytes, l.Sha256))];
    }
}
