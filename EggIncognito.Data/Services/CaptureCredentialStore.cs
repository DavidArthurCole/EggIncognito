using EggIncognito.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Data.Services;

// Proxy tokens + per-user capture CAs for hosted capture. Tokens are stored SHA-256 hashed; the
// plaintext exists only in the mint response. Scoped like the other DB services; the front door
// reaches it through an injected lookup that opens a scope per call.
public sealed class CaptureCredentialStore(EggIncognitoDbContext db)
{
    public static string Hash(string token) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(token)));

    public static string MintToken() =>
        Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24));

    public async Task SetTokenAsync(string discordId, string hash, CancellationToken ct = default)
    {
        var row = await db.CaptureProxyTokens.FirstOrDefaultAsync(t => t.DiscordId == discordId, ct);
        if (row is null)
            db.CaptureProxyTokens.Add(new CaptureProxyToken { DiscordId = discordId, TokenHash = hash });
        else
            row.TokenHash = hash;
        await db.SaveChangesAsync(ct);
    }

    public async Task<string?> GetTokenHashAsync(string discordId, CancellationToken ct = default) =>
        (await db.CaptureProxyTokens.AsNoTracking()
            .FirstOrDefaultAsync(t => t.DiscordId == discordId, ct))?.TokenHash;

    // Resolves the token hash by either the Discord id (the numeric NameIdentifier) or the Discord
    // username, so the proxy username field can be the memorable handle, not just the id. Id is tried
    // first (no join); a username falls back through the User table. Username match is case-insensitive.
    public async Task<string?> GetTokenHashByIdOrUsernameAsync(string idOrUsername, CancellationToken ct = default)
    {
        var byId = await GetTokenHashAsync(idOrUsername, ct);
        if (byId is not null) return byId;
        var discordId = await db.Users.AsNoTracking()
            .Where(u => u.Username.ToLower() == idOrUsername.ToLower())
            .Select(u => u.DiscordId)
            .FirstOrDefaultAsync(ct);
        return discordId is null ? null : await GetTokenHashAsync(discordId, ct);
    }

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
