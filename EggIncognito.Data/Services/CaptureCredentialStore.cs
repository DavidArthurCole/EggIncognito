using EggIncognito.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Data.Services;

public sealed class CaptureCredentialStore(EggIncognitoDbContext db) {
    public async Task SaveCaAsync(Guid userId, byte[] pfx, string thumbprint, CancellationToken ct = default) {
        var row = await db.CaptureUserCas.FirstOrDefaultAsync(c => c.UserId == userId, ct);
        if (row is null) {
            db.CaptureUserCas.Add(new CaptureUserCa { UserId = userId, Pfx = pfx, Thumbprint = thumbprint });
        } else {
            row.Pfx = pfx;
            row.Thumbprint = thumbprint;
        }
        await db.SaveChangesAsync(ct);
    }

    public Task<CaptureUserCa?> GetCaAsync(Guid userId, CancellationToken ct = default) =>
        db.CaptureUserCas.AsNoTracking().FirstOrDefaultAsync(c => c.UserId == userId, ct);
}
