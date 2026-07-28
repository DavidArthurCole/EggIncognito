using System.Security.Cryptography;
using System.Text;
using EggIncognito.Core.Services.Devices;
using EggIncognito.Data.Services;
using EggIncognito.Runner.Data;

namespace EggIncognito.Runner.Trigger;

public sealed record ProbeApiResult(int Status, string? DeviceId, object? Body, string? Error);

public sealed class DeviceProbeApi(string secret, RunnerDb db, IProcessRunner runner, TimeProvider time, ILoggerFactory logs) {
    private readonly ILogger _logger = logs.CreateLogger("DeviceProbeApi");

    public async Task<ProbeApiResult> ProbeOneAsync(string? authorizationHeader, string id, string triggeredBy) {
        if (!BearerMatches(authorizationHeader)) return new ProbeApiResult(401, id, null, "unauthorized");
        using var ctx = db.NewContext();
        var store = new DeviceStatusStore(ctx);
        var device = await store.GetAsync(id, CancellationToken.None);
        if (device is null) return new ProbeApiResult(404, id, null, "unknown device");
        try {
            var row = await DeviceProbeRunner.ProbeOneAsync(device, triggeredBy, runner, store, ctx, _logger, time, CancellationToken.None);
            return new ProbeApiResult(200, id, Project(row), null);
        } catch (Exception ex) {
            return new ProbeApiResult(500, id, null, ex.Message);
        }
    }

    public async Task<ProbeApiResult> ProbeAllAsync(string? authorizationHeader, string triggeredBy) {
        if (!BearerMatches(authorizationHeader)) return new ProbeApiResult(401, null, null, "unauthorized");
        using var ctx = db.NewContext();
        var store = new DeviceStatusStore(ctx);
        var devices = await store.EnabledDevicesAsync(CancellationToken.None);
        var n = 0;
        foreach (var d in devices) {
            try {
                await DeviceProbeRunner.ProbeOneAsync(d, triggeredBy, runner, store, ctx, _logger, time, CancellationToken.None);
                n++;
            } catch (Exception ex) {
                _logger.LogWarning(ex, "probe failed for {DeviceId}", d.Id);
            }
        }
        return new ProbeApiResult(200, null, new { probed = n }, null);
    }

    private static object Project(EggIncognito.Data.Models.DeviceProbe row) => new {
        id = row.DeviceId,
        reachable = row.Reachable,
        installedAppVersion = row.InstalledAppVersion,
        installedBuild = row.InstalledBuild,
        latestAvailable = row.LatestAvailable,
        result = row.Result,
        note = row.Note,
        probedAt = row.ProbedAt,
    };

    private bool BearerMatches(string? header) {
        const string prefix = "Bearer ";
        if (header is null || !header.StartsWith(prefix, StringComparison.Ordinal)) return false;
        var presented = Encoding.UTF8.GetBytes(header[prefix.Length..]);
        var expected = Encoding.UTF8.GetBytes(secret);
        return CryptographicOperations.FixedTimeEquals(presented, expected);
    }
}
