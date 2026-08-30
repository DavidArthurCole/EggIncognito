using EggIncognito.Core.Services.Devices;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.Models.Devices;
using EggIncognito.Services.Auth;
using EggIncognito.Services.Devices;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EggIncognito.Controllers;

[ApiController]
[Route("api/devices/virtual")]
[ApiAccess(ApiAccessLevel.Admin)]
[EnableRateLimiting("write")]
public sealed class VirtualDevicesController(
    IServiceProvider services,
    VirtualDeviceConfig config,
    VirtualDeviceLifecycle lifecycle) : ControllerBase {
    private ProvisionedInstanceStore? Store =>
        services.GetService(typeof(ProvisionedInstanceStore)) as ProvisionedInstanceStore;

    private DeviceCaptureManager? Captures =>
        services.GetService(typeof(DeviceCaptureManager)) as DeviceCaptureManager;

    [HttpGet]
    [EnableRateLimiting("read")]
    public async Task<IActionResult> List(CancellationToken ct) {
        var listed = await lifecycle.Provisioner.ListAsync(ct);
        var containers = new Dictionary<string, ProvisionedInstance>(StringComparer.Ordinal);
        foreach (var c in listed.Value ?? []) containers[c.InstanceId] = c;

        List<VirtualInstanceRow> rows;
        if (lifecycle.RemoteOwned) {
            await lifecycle.MirrorRemoteDevicesAsync(containers.Values, ct);
            rows = [.. containers.Values.Select(RemoteRow)];
        } else {
            var store = Store;
            if (store is null) {
                return StatusCode(503, new {
                    error = "local provisioning is not registered here; it needs a database and the real device stack, "
                            + "or set Devices:Virtual:Kind to remote to provision on another host"
                });
            }

            rows = [.. (await store.AllAsync(ct)).Select(r => Row(r, containers))];
        }

        var byState = rows.GroupBy(r => r.State, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        return Ok(new VirtualDevicesStatus(
            config.Enabled,
            listed.Ok,
            config.Kind,
            config.Image,
            config.MaxInstances,
            rows.Count(r => ProvisionStates.IsLive(r.State)),
            listed.Note ?? lifecycle.SupportNote,
            byState,
            rows));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] VirtualCreateRequest? request, CancellationToken ct) {
        if (!config.Enabled) return StatusCode(503, new { error = "virtual devices are disabled" });
        var res = await lifecycle.CreateAsync(request?.Image, ct);
        var payload = new VirtualActionResult(
            res.Ok, DeviceOutcomes.Label(res.Outcome), res.Value?.InstanceId, res.Note);
        return res.Ok ? Ok(payload) : StatusCode(res.Outcome == DeviceOutcome.Unsupported ? 503 : 400, payload);
    }

    [HttpDelete("{instanceId}")]
    public async Task<IActionResult> Destroy(string instanceId, CancellationToken ct) {
        var res = await lifecycle.DestroyAsync(instanceId, ct);
        var payload = new VirtualActionResult(res.Ok, DeviceOutcomes.Label(res.Outcome), instanceId, res.Note);
        return res.Ok ? Ok(payload) : StatusCode(res.Outcome == DeviceOutcome.Unsupported ? 503 : 409, payload);
    }

    [HttpPost("reconcile")]
    public async Task<IActionResult> Reconcile(CancellationToken ct) {
        int touched = await lifecycle.ReconcileAsync(ct);
        return Ok(new VirtualActionResult(true, DeviceOutcomes.Ok, null, $"reconciled {touched} instance(s)"));
    }

    private static VirtualInstanceRow RemoteRow(ProvisionedInstance instance) => new(
        instance.InstanceId, instance.Kind, instance.Image, instance.State, instance.AdbSerial, instance.DeviceId,
        instance.CreatedAt, null, instance.Note, true, instance.Note, 0, null);

    private VirtualInstanceRow Row(
        ProvisionedInstanceRow row, Dictionary<string, ProvisionedInstance> containers) {
        containers.TryGetValue(row.InstanceId, out var container);
        long flows = 0;
        string? lastFlow = null;
        if (row.DeviceId is { Length: > 0 } deviceId && Captures is { } captures) {
            flows = captures.DiagFor(deviceId).Flows;
            var snap = captures.HubFor(deviceId)?.Snapshot();
            if (snap is { Count: > 0 }) lastFlow = snap[^1].Timestamp;
        }

        return new VirtualInstanceRow(
            row.InstanceId, row.Kind, row.Image, row.State,
            row.AdbSerial, row.DeviceId, row.CreatedAt, row.LastSeenAt, row.Note,
            container is not null, container?.Note, flows, lastFlow);
    }
}
