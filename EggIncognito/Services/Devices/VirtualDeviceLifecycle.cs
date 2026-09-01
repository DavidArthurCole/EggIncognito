using EggIncognito.Core.Services.Devices;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;

namespace EggIncognito.Services.Devices;

public sealed class VirtualDeviceLifecycle(
    IServiceScopeFactory scopeFactory,
    IDeviceProvisioners provisioners,
    VirtualDeviceConfig config,
    IProcessRunner runner,
    TimeProvider time,
    ILogger<VirtualDeviceLifecycle> logger) : BackgroundService {
    private const string Package = "com.auxbrain.egginc";
    private static readonly TimeSpan AdbTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan BootstrapBackoff = TimeSpan.FromMinutes(15);
    private readonly SemaphoreSlim _gate = new(1, 1);
#pragma warning disable IDE0028
    private readonly Dictionary<string, DateTimeOffset> _lastBootstrap = new(StringComparer.Ordinal);
#pragma warning restore IDE0028

    public bool Supported { get; private set; }
    public string? SupportNote { get; private set; }

    public IDeviceProvisioner Provisioner => provisioners.For(config.Kind);

    public bool RemoteOwned => RemoteDeviceProvisioner.IsRemoteKind(config.Kind);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        if (!config.Enabled) {
            logger.LogInformation("virtual devices: disabled (Devices:Virtual:Enabled is false)");
            return;
        }

        if (RemoteOwned) {
            logger.LogInformation(
                "virtual devices: kind '{Kind}' - instances are owned and reconciled by the remote host, "
                + "this instance runs no reconciler and writes no provisioned_instances rows", config.Kind);
            return;
        }

        var probe = await Provisioner.ListAsync(stoppingToken);
        Supported = probe.Ok;
        SupportNote = probe.Note;
        if (!probe.Ok) {
            logger.LogWarning("virtual devices: provisioner '{Kind}' is not usable ({Outcome}): {Note}",
                config.Kind, DeviceOutcomes.Label(probe.Outcome), probe.Note ?? "no detail");
        } else {
            logger.LogInformation("virtual devices: provisioner '{Kind}' ready, {Count} container(s) present",
                config.Kind, probe.Value?.Count ?? 0);
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, config.ReconcileSeconds)), time);
        try {
            await ReconcileAsync(stoppingToken);
            while (await timer.WaitForNextTickAsync(stoppingToken)) await ReconcileAsync(stoppingToken);
        } catch (OperationCanceledException ex) {
            logger.LogDebug(ex, "virtual devices: reconcile loop cancelled");
        }
    }

    public async Task<DeviceResult<ProvisionedInstance>> CreateAsync(string? image, CancellationToken ct) {
        if (!config.Enabled) return DeviceResult<ProvisionedInstance>.Unsupported("virtual devices are disabled");
        if (RemoteOwned) return await Provisioner.CreateAsync(new ProvisionSpec(config.Kind, image ?? ""), ct);

        using var scope = scopeFactory.CreateScope();
        if (scope.ServiceProvider.GetService(typeof(ProvisionedInstanceStore)) is not ProvisionedInstanceStore store)
            return DeviceResult<ProvisionedInstance>.Unsupported("no database configured");

        int live = await store.CountLiveAsync(ct);
        if (live >= config.MaxInstances) {
            return DeviceResult<ProvisionedInstance>.Error(
                $"virtual device cap reached ({live}/{config.MaxInstances}); destroy one before creating another");
        }

        var created = await Provisioner.CreateAsync(
            new ProvisionSpec(config.Kind, string.IsNullOrWhiteSpace(image) ? config.Image : image), ct);
        if (!created.Ok || created.Value is not { } instance) return created;

        await store.AddAsync(instance, ct);
        return created;
    }

    public async Task MirrorRemoteDevicesAsync(IEnumerable<ProvisionedInstance> instances, CancellationToken ct) {
        if (!RemoteOwned) return;

        using var scope = scopeFactory.CreateScope();
        if (scope.ServiceProvider.GetService(typeof(IDeviceStatusStore)) is not IDeviceStatusStore devices) return;

        foreach (var instance in instances) {
            if (instance.DeviceId is not { Length: > 0 } deviceId) continue;
            if (!ProvisionStates.IsLive(instance.State)) {
                await devices.RemoveAsync(deviceId, ct);
                continue;
            }

            if (instance.AdbSerial is not { Length: > 0 } serial) continue;
            var existing = await devices.GetAsync(deviceId, ct);
            if (existing is not null && existing.Enabled && existing.Target == serial) continue;

            await devices.UpsertDeviceAsync(deviceId, Platforms.Android, deviceId, serial, Package,
                DeviceOrigins.Virtual, ct);
            logger.LogInformation(
                "virtual devices: mirrored remote device {Id} on {Serial} so the console can reach it over the bridge",
                deviceId, serial);
        }
    }

    public async Task<DeviceResult> DestroyAsync(string instanceId, CancellationToken ct) {
        if (RemoteOwned) {
            var remote = await Provisioner.DestroyAsync(instanceId, ct);
            if (remote.Ok) await ForgetRemoteDeviceAsync(instanceId, ct);
            return remote;
        }

        using var scope = scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        if (sp.GetService(typeof(ProvisionedInstanceStore)) is not ProvisionedInstanceStore store)
            return DeviceResult.Unsupported("no database configured");

        var row = await store.GetAsync(instanceId, ct);
        if (row is null) return DeviceResult.Error($"unknown virtual device '{instanceId}'");

        if (row.DeviceId is { Length: > 0 } deviceId
            && sp.GetService(typeof(DeviceJobStore)) is DeviceJobStore jobs) {
            var running = await jobs.RunningAsync(ct);
            if (running.FirstOrDefault(j => string.Equals(j.DeviceId, deviceId, StringComparison.Ordinal)) is { } job) {
                return DeviceResult.Error(
                    $"device job '{job.Kind}' is running on {deviceId}; destroy again once it finishes");
            }
        }

        var destroyed = await Provisioner.DestroyAsync(instanceId, ct);
        if (!destroyed.Ok) return destroyed;

        if (row.DeviceId is { Length: > 0 } id && sp.GetService(typeof(IDeviceStatusStore)) is IDeviceStatusStore ds)
            await ds.RemoveAsync(id, ct);
        await store.RemoveAsync(instanceId, ct);
        _lastBootstrap.Remove(instanceId);
        return DeviceResult.Success(destroyed.Note);
    }

    private async Task ForgetRemoteDeviceAsync(string instanceId, CancellationToken ct) {
        using var scope = scopeFactory.CreateScope();
        if (scope.ServiceProvider.GetService(typeof(IDeviceStatusStore)) is not IDeviceStatusStore devices) return;
        await devices.RemoveAsync(instanceId, ct);
    }

    public async Task<int> ReconcileAsync(CancellationToken ct) {
        if (!config.Enabled || RemoteOwned) return 0;
        if (!await _gate.WaitAsync(TimeSpan.Zero, ct)) return 0;
        try {
            return await ReconcileCoreAsync(ct);
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            logger.LogWarning(ex, "virtual devices: reconcile pass failed, retrying next tick");
            return 0;
        } finally {
            _gate.Release();
        }
    }

    private async Task<int> ReconcileCoreAsync(CancellationToken ct) {
        var listed = await Provisioner.ListAsync(ct);
        Supported = listed.Ok;
        SupportNote = listed.Note;
        if (!listed.Ok || listed.Value is not { } containers) return 0;

        using var scope = scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        if (sp.GetService(typeof(ProvisionedInstanceStore)) is not ProvisionedInstanceStore store) return 0;
        if (sp.GetService(typeof(IDeviceStatusStore)) is not IDeviceStatusStore devices) return 0;

        var byId = containers.ToDictionary(c => c.InstanceId, StringComparer.Ordinal);
        int touched = 0;

        foreach (var row in await store.ReconcilableAsync(ct)) {
            if (!byId.TryGetValue(row.InstanceId, out var container)) {
                if (row.State == ProvisionStates.Failed) continue;
                await store.SetStateAsync(row.InstanceId, ProvisionStates.Failed, "container is gone", ct);
                logger.LogWarning("virtual devices: {Id} container vanished, marked failed", row.InstanceId);
                touched++;
                continue;
            }

            await store.TouchAsync(row.InstanceId, container.HostRef, container.AdbSerial, ct);
            touched++;

            if (container.State == ProvisionStates.Stopped) {
                if (row.State != ProvisionStates.Stopped)
                    await store.SetStateAsync(row.InstanceId, ProvisionStates.Stopped, "container is not running", ct);
                continue;
            }

            if ((container.AdbSerial ?? row.AdbSerial) is not { Length: > 0 } serial) {
                if (row.State != ProvisionStates.Booting)
                    await store.SetStateAsync(row.InstanceId, ProvisionStates.Booting,
                        "waiting for the container to get an address", ct);
                continue;
            }

            if (!await BootCompletedAsync(serial, ct)) {
                if (row.State == ProvisionStates.Creating)
                    await store.SetStateAsync(row.InstanceId, ProvisionStates.Booting,
                        "waiting for sys.boot_completed", ct);
                continue;
            }

            if (row.State != ProvisionStates.Ready)
                await store.SetStateAsync(row.InstanceId, ProvisionStates.Ready, "android boot completed", ct);

            string deviceId = await EnsureDeviceRowAsync(row, serial, devices, store, ct);
            await EnsureAppInstalledAsync(row.InstanceId, deviceId, serial, store, ct);
        }

        return touched;
    }

    private async Task<string> EnsureDeviceRowAsync(
        ProvisionedInstanceRow row, string serial, IDeviceStatusStore devices, ProvisionedInstanceStore store,
        CancellationToken ct) {
        string deviceId = row.DeviceId ?? row.InstanceId;
        var existing = await devices.GetAsync(deviceId, ct);
        if (existing is null || !existing.Enabled || existing.Target != serial) {
            await devices.UpsertDeviceAsync(deviceId, Platforms.Android, deviceId, serial, Package,
                DeviceOrigins.Virtual, ct);
            logger.LogInformation("virtual devices: registered {Id} as an android device on {Serial}",
                deviceId, serial);
        }

        if (row.DeviceId is null) await store.SetDeviceAsync(row.InstanceId, deviceId, ct);
        return deviceId;
    }

    private async Task EnsureAppInstalledAsync(
        string instanceId, string deviceId, string serial, ProvisionedInstanceStore store, CancellationToken ct) {
        var pm = await Adb(["-s", serial, "shell", $"pm path {Package}"], AdbTimeout, ct);
        if (pm.ExitCode == 0 && pm.Stdout.Contains("package:", StringComparison.Ordinal)) {
            _lastBootstrap.Remove(instanceId);
            return;
        }

        if (_lastBootstrap.TryGetValue(instanceId, out var lastTry)
            && time.GetUtcNow() - lastTry < BootstrapBackoff)
            return;
        _lastBootstrap[instanceId] = time.GetUtcNow();

        using var scope = scopeFactory.CreateScope();
        if (scope.ServiceProvider.GetService(typeof(DeviceCookbookRunner)) is not DeviceCookbookRunner cookbookRunner) {
            logger.LogWarning("virtual devices: {Id} cannot bring up egg inc, no cookbook runner (db disabled?)",
                instanceId);
            return;
        }

        var run = await cookbookRunner.RunNowAsync(
            deviceId, new DeviceCookbookRequest(DeviceCookbookIds.BringUp, null), "auto:provision", ct);
        if (!run.Ok) {
            await store.SetStateAsync(instanceId, ProvisionStates.Failed,
                run.Note ?? $"bring-up failed at {run.FailedStep ?? "?"}", ct);
            return;
        }

        await store.SetStateAsync(instanceId, ProvisionStates.Ready, run.Note ?? "egg inc brought up", ct);
        logger.LogInformation("virtual devices: {Id} brought up egg inc on {Device}", instanceId, deviceId);
    }

    private async Task<bool> BootCompletedAsync(string serial, CancellationToken ct) {
        await Adb(["connect", serial], AdbTimeout, ct);
        var boot = await Adb(["-s", serial, "shell", "getprop sys.boot_completed"], AdbTimeout, ct);
        return boot.ExitCode == 0 && boot.Stdout.Trim() == "1";
    }

    private async Task<ProcessResult> Adb(string[] args, TimeSpan timeout, CancellationToken ct) {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        return await runner.RunAsync("adb", args, cts.Token);
    }

    public override void Dispose() {
        base.Dispose();
        _gate.Dispose();
    }
}
