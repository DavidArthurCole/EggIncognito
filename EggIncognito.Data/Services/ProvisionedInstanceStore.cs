using EggIncognito.Core.Services.Devices;
using EggIncognito.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Data.Services;

public sealed class ProvisionedInstanceStore(EggIncognitoDbContext db, TimeProvider time) {
    private static readonly string[] LiveStates =
        [ProvisionStates.Creating, ProvisionStates.Booting, ProvisionStates.Ready, ProvisionStates.Stopped];

    private static readonly string[] ReconcileStates =
        [.. LiveStates, ProvisionStates.Failed];

    public Task<List<ProvisionedInstanceRow>> AllAsync(CancellationToken ct = default) =>
        db.ProvisionedInstances.AsNoTracking().OrderBy(x => x.CreatedAt).ToListAsync(ct);

    public Task<List<ProvisionedInstanceRow>> ReconcilableAsync(CancellationToken ct = default) =>
        db.ProvisionedInstances.AsNoTracking()
            .Where(x => ReconcileStates.Contains(x.State))
            .OrderBy(x => x.CreatedAt).ToListAsync(ct);

    public Task<int> CountLiveAsync(CancellationToken ct = default) =>
        db.ProvisionedInstances.CountAsync(x => LiveStates.Contains(x.State), ct);

    public Task<ProvisionedInstanceRow?> GetAsync(string instanceId, CancellationToken ct = default) =>
        db.ProvisionedInstances.AsNoTracking().FirstOrDefaultAsync(x => x.InstanceId == instanceId, ct);

    public async Task AddAsync(ProvisionedInstance instance, CancellationToken ct = default) {
        db.ProvisionedInstances.Add(new ProvisionedInstanceRow {
            InstanceId = instance.InstanceId,
            Kind = instance.Kind,
            Image = instance.Image,
            State = instance.State,
            AdbSerial = instance.AdbSerial,
            HostRef = instance.HostRef,
            CreatedAt = instance.CreatedAt == default ? time.GetUtcNow() : instance.CreatedAt,
            LastSeenAt = time.GetUtcNow(),
            Note = instance.Note
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task SetStateAsync(string instanceId, string state, string? note, CancellationToken ct = default) {
        var row = await db.ProvisionedInstances.FirstOrDefaultAsync(x => x.InstanceId == instanceId, ct);
        if (row is null) return;
        row.State = state;
        row.Note = note ?? row.Note;
        row.LastSeenAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
    }

    public async Task SetDeviceAsync(string instanceId, string deviceId, CancellationToken ct = default) {
        var row = await db.ProvisionedInstances.FirstOrDefaultAsync(x => x.InstanceId == instanceId, ct);
        if (row is null) return;
        row.DeviceId = deviceId;
        row.LastSeenAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
    }

    public async Task TouchAsync(
        string instanceId, string? hostRef, string? adbSerial = null, CancellationToken ct = default) {
        var row = await db.ProvisionedInstances.FirstOrDefaultAsync(x => x.InstanceId == instanceId, ct);
        if (row is null) return;
        row.LastSeenAt = time.GetUtcNow();
        if (!string.IsNullOrEmpty(hostRef)) row.HostRef = hostRef;
        if (!string.IsNullOrEmpty(adbSerial)) row.AdbSerial = adbSerial;
        await db.SaveChangesAsync(ct);
    }

    public async Task DisableDeviceAsync(string deviceId, CancellationToken ct = default) {
        var row = await db.Devices.FirstOrDefaultAsync(d => d.Id == deviceId, ct);
        if (row is null || !row.Enabled) return;
        row.Enabled = false;
        await db.SaveChangesAsync(ct);
    }
}
