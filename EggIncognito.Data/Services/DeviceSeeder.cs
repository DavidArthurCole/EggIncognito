using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Data.Services;

// Mirrors the config device roster into the devices table on boot. Config is authoritative: upsert
// declared devices (Enabled=true), mark any DB device absent from config Enabled=false (kept for FK
// integrity of its probe history). Takes flat tuples so Data needs no reference to the App's DeviceEntry.
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
        var stale = await db.Devices.Where(x => x.Enabled && !declared.Contains(x.Id)).ToListAsync(ct);
        foreach (var s in stale) s.Enabled = false;
        if (stale.Count > 0) await db.SaveChangesAsync(ct);
    }
}
