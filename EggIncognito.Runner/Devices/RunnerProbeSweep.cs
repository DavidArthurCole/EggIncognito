using EggIncognito.Core.Services.Devices;
using EggIncognito.Data.Services;
using EggIncognito.Runner.Data;

namespace EggIncognito.Runner.Devices;

public static class RunnerProbeSweep {
    public static async Task RunAsync(RunnerDb db, IProcessRunner runner, TimeProvider time, ILogger logger, CancellationToken ct) {
        using var ctx = db.NewContext();
        var store = new DeviceStatusStore(ctx);
        var jobs = new DeviceJobStore(ctx, time);
        var devices = await store.EnabledDevicesAsync(ct);
        foreach (var d in devices) {
            if (ct.IsCancellationRequested) break;
            try {
                await DeviceProbeRunner.ProbeOneAsync(d, "poll", runner, jobs, ctx, logger, time, ct);
            } catch (Exception ex) {
                logger.LogWarning(ex, "probe failed for {DeviceId}", d.Id);
            }
        }
    }
}
