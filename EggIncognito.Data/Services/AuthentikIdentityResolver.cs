using EggIncognito.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Data.Services;

// Resolves a user_id for an Authentik OIDC login: exact-match an existing authentik identity,
// else auto-link via a matching discord identity (Authentik's Discord source surfaces the
// federated Discord snowflake as the discord_id claim), else create a brand-new account. Done
// inside one DB transaction so a concurrent identical login can't leave the loser with an
// orphaned user_id: if this identities insert loses the (provider, subject) race, re-select and
// return the winner's user_id instead of trusting the row this call built.
public sealed class AuthentikIdentityResolver(EggIncognitoDbContext db)
{
    public async Task<Guid> ResolveAsync(string sub, string? discordId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var existing = await LookupAsync("authentik", sub, ct);
        if (existing is { } found)
        {
            await tx.CommitAsync(ct);
            return found;
        }

        Guid userId;
        if (!string.IsNullOrEmpty(discordId) && await LookupAsync("discord", discordId, ct) is { } linked)
        {
            userId = linked;
        }
        else
        {
            userId = Guid.NewGuid();
            db.Users.Add(new User { UserId = userId, DiscordId = null, Role = "viewer", Username = "", CreatedAt = DateTimeOffset.UtcNow, LastLoginAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync(ct);
        }

        var winnerId = await InsertIdentityAsync(userId, sub, ct);

        await tx.CommitAsync(ct);
        return winnerId;
    }

    private async Task<Guid?> LookupAsync(string provider, string subject, CancellationToken ct)
        => await db.Identities.Where(i => i.Provider == provider && i.Subject == subject)
            .Select(i => (Guid?)i.UserId).FirstOrDefaultAsync(ct);

    // Inserts the (provider, subject) -> userId link. If a concurrent resolve already won this
    // exact (provider, subject) pair, ON CONFLICT DO NOTHING makes this a no-op: re-select and
    // return the winner's user_id rather than the caller's freshly-built one, closing the race
    // gap noted (but left open) in EggLedger's AuthentikIdentityResolver.
    private async Task<Guid> InsertIdentityAsync(Guid userId, string sub, CancellationToken ct)
    {
        var affected = await db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO identities (user_id, provider, subject) VALUES ({userId}, 'authentik', {sub}) ON CONFLICT (provider, subject) DO NOTHING",
            ct);
        if (affected > 0) return userId;

        var winner = await LookupAsync("authentik", sub, ct);
        return winner ?? userId;
    }
}
