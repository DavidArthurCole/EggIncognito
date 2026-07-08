using EggIncognito.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Data.Services;

public sealed class CaptureCredentialStore(EggIncognitoDbContext db)
{
    public async Task SaveCaAsync(string discordId, byte[] pfx, string thumbprint, CancellationToken ct = default)
    {
        var row = await db.CaptureUserCas.FirstOrDefaultAsync(c => c.DiscordId == discordId, ct);
        if (row is null)
            db.CaptureUserCas.Add(new CaptureUserCa { DiscordId = discordId, Pfx = pfx, Thumbprint = thumbprint });
        else
        {
            row.Pfx = pfx;
            row.Thumbprint = thumbprint;
        }
        await db.SaveChangesAsync(ct);
    }

    public Task<CaptureUserCa?> GetCaAsync(string discordId, CancellationToken ct = default) =>
        db.CaptureUserCas.AsNoTracking().FirstOrDefaultAsync(c => c.DiscordId == discordId, ct);
}
