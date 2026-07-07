using EggIncognito.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Data.Services;

// Backs Authentik back-channel logout: RevokeAsync records a sid the IdP told us ended; IsRevokedAsync
// is checked on every cookie-auth request (OnValidatePrincipal) so that session stops working
// immediately instead of riding out its 30-day sliding cookie.
public sealed class SessionRevocationStore(EggIncognitoDbContext db)
{
    public async Task RevokeAsync(string sid, CancellationToken ct)
    {
        var exists = await db.RevokedSessions.AnyAsync(r => r.Sid == sid, ct);
        if (exists) return;
        db.RevokedSessions.Add(new RevokedSession { Sid = sid });
        await db.SaveChangesAsync(ct);
    }

    public Task<bool> IsRevokedAsync(string sid, CancellationToken ct)
        => db.RevokedSessions.AnyAsync(r => r.Sid == sid, ct);
}
