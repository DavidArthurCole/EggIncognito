using EggIncognito.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Data.Services;

public interface IDeviceStatusStore {
    Task UpsertDeviceAsync(string id, string platform, string label, string target, string package,
        CancellationToken ct = default);

    Task<List<Device>> EnabledDevicesAsync(CancellationToken ct = default);
    Task<Device?> GetAsync(string id, CancellationToken ct = default);
}

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
}
