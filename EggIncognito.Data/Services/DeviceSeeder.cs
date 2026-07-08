using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Data.Services;

public static class DeviceSeeder
{
    public static async Task SeedAsync(
        IDeviceStatusStore store, EggIncognitoDbContext db,
        IReadOnlyList<(string Id, string Platform, string Label, string Target, string Package)> devices,
        CancellationToken ct = default)
    {
        var declared = new HashSet<string>();
        foreach (var d in devices)
        {
            await store.UpsertDeviceAsync(d.Id, d.Platform, d.Label, d.Target, d.Package, ct);
            declared.Add(d.Id);
        }
        // An empty config means "this instance manages no devices", not "disable every device".
        if (declared.Count == 0) return;
        var stale = await db.Devices.Where(x => x.Enabled && !declared.Contains(x.Id)).ToListAsync(ct);
        foreach (var s in stale) s.Enabled = false;
        if (stale.Count > 0) await db.SaveChangesAsync(ct);
    }
}
