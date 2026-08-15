using EggIncognito.Core.Services.Devices;
using EggIncognito.Data.Services;
using EggIncognito.Runner.Data;

namespace EggIncognito.Runner.Trigger;

public sealed record ProbeApiResult(int Status, string? DeviceId, object? Body, string? Error);

public sealed class DeviceProbeApi(string secret, RunnerDb db, IProcessRunner runner, TimeProvider time, ILoggerFactory logs) {
    private readonly ILogger _logger = logs.CreateLogger("DeviceProbeApi");

    public async Task<ProbeApiResult> ProbeOneAsync(string? authorizationHeader, string id, string triggeredBy) {
        if (!BearerAuth.Matches(authorizationHeader, secret)) return new ProbeApiResult(401, id, null, "unauthorized");
        using var ctx = db.NewContext();
        var store = new DeviceStatusStore(ctx);
        var jobs = new DeviceJobStore(ctx, time);
        var device = await store.GetAsync(id, CancellationToken.None);
        if (device is null) return new ProbeApiResult(404, id, null, "unknown device");
        try {
            var row = await DeviceProbeRunner.ProbeOneAsync(device, triggeredBy, runner, jobs, ctx, _logger, time, CancellationToken.None);
            return new ProbeApiResult(200, id, Project(row), null);
        } catch (Exception ex) {
            return new ProbeApiResult(500, id, null, ex.Message);
        }
    }

    public async Task<ProbeApiResult> ProbeAllAsync(string? authorizationHeader, string triggeredBy) {
        if (!BearerAuth.Matches(authorizationHeader, secret)) return new ProbeApiResult(401, null, null, "unauthorized");
        using var ctx = db.NewContext();
        var store = new DeviceStatusStore(ctx);
        var jobs = new DeviceJobStore(ctx, time);
        var devices = await store.EnabledDevicesAsync(CancellationToken.None);
        var n = 0;
        foreach (var d in devices) {
            try {
                await DeviceProbeRunner.ProbeOneAsync(d, triggeredBy, runner, jobs, ctx, _logger, time, CancellationToken.None);
                n++;
            } catch (Exception ex) {
                _logger.LogWarning(ex, "probe failed for {DeviceId}", d.Id);
            }
        }
        return new ProbeApiResult(200, null, new { probed = n }, null);
    }

    private static object Project(DeviceJobRow row) => new {
        id = row.DeviceId,
        reachable = row.Reachable == true,
        installedAppVersion = row.AppVersion,
        installedBuild = row.Build,
        latestAvailable = (string?)null,
        result = row.Outcome ?? "",
        note = row.Message,
        probedAt = row.StartedAt,
    };
}
