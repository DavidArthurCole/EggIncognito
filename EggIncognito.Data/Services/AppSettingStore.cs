using EggIncognito.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Data.Services;

public sealed class AppSettingStore(EggIncognitoDbContext db, TimeProvider time) {
    public const string VirtualImageOverrideKey = "devices.virtual.image";

    public async Task<string?> GetAsync(string key, CancellationToken ct) {
        var row = await db.AppSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == key, ct);
        return row?.Value;
    }

    public async Task SetAsync(string key, string value, CancellationToken ct) {
        var row = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == key, ct);
        if (row is null) {
            db.AppSettings.Add(new AppSetting { Key = key, Value = value, UpdatedAt = time.GetUtcNow() });
        } else {
            row.Value = value;
            row.UpdatedAt = time.GetUtcNow();
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task ClearAsync(string key, CancellationToken ct) {
        var row = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == key, ct);
        if (row is null) return;
        db.AppSettings.Remove(row);
        await db.SaveChangesAsync(ct);
    }
}
