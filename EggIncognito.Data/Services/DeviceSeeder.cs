using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Data.Services;

public static class DeviceSeeder {
    public static async Task SeedAsync(
        IDeviceStatusStore store, EggIncognitoDbContext db,
        IReadOnlyList<(string Id, string Platform, string Label, string Target, string Package)> devices,
        CancellationToken ct = default) {
        var declared = new HashSet<string>();
        foreach ((string Id, string Platform, string Label, string Target, string Package) in devices) {
            await store.UpsertDeviceAsync(Id, Platform, Label, Target, Package, ct);
            declared.Add(Id);
        }

        if (declared.Count == 0) return;
        var stale = await db.Devices.Where(x => x.Enabled && !declared.Contains(x.Id)).ToListAsync(ct);
        foreach (var s in stale) s.Enabled = false;
        if (stale.Count > 0) await db.SaveChangesAsync(ct);
    }
}
