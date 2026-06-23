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
        // Disable-stale ONLY when this instance actually declares devices. An empty config means "this
        // instance manages no devices" (e.g. a dev run sharing the prod DB), NOT "disable every device" -
        // the latter nuked the farm roster for all viewers once. With no declared devices we upsert nothing
        // and disable nothing; the owning instance stays authoritative.
        if (declared.Count == 0) return;
        var stale = await db.Devices.Where(x => x.Enabled && !declared.Contains(x.Id)).ToListAsync(ct);
        foreach (var s in stale) s.Enabled = false;
        if (stale.Count > 0) await db.SaveChangesAsync(ct);
    }
}
