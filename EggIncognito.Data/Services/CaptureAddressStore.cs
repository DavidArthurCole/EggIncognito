using System.Net;
using System.Security.Cryptography;
using System.Text;
using EggIncognito.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Data.Services;

// Issues + reverse-maps per-user IPv6 proxy addresses. Each user has ONE stored address with a random
// host part inside the configured prefix. It is the capture credential (connecting to it proves you
// were issued it), so it is treated as a leakable bearer token: stable across sessions for convenience,
// but ROTATABLE on demand. Rotating overwrites the stored address, so the old one resolves to nobody
// (the front door then rejects it). The `secret` parameter is retained for signature compatibility but
// the address is random, not derived, so a leaked address tells an attacker nothing about future ones.
public sealed class CaptureAddressStore(EggIncognitoDbContext db)
{
    readonly EggIncognitoDbContext _db = db;

    // Build a fresh random address: prefix bits fixed (per the CIDR length), host bits cryptographically
    // random. Honors any prefix length 0..128 (e.g. a /65 sub-prefix sharing the /64 with the host's own
    // addresses). Reserved host parts (all-zero, ::1) are bumped to ::2.
    public static IPAddress RandomInPrefix(string prefixCidr)
    {
        var parts = prefixCidr.Split('/');
        var prefix = IPAddress.Parse(parts[0]).GetAddressBytes(); // 16 bytes
        var prefixLen = parts.Length > 1 ? int.Parse(parts[1]) : 64;
        var rnd = RandomNumberGenerator.GetBytes(16);

        var bytes = new byte[16];
        for (var i = 0; i < 16; i++)
        {
            var firstBit = i * 8;
            if (firstBit + 8 <= prefixLen) { bytes[i] = prefix[i]; continue; }   // fully inside the prefix
            if (firstBit >= prefixLen) { bytes[i] = rnd[i]; continue; }           // fully a host byte
            // Straddling byte: high (prefixLen - firstBit) bits from the prefix, the rest random.
            var prefixBits = prefixLen - firstBit;
            var mask = (byte)(0xFF << (8 - prefixBits));
            bytes[i] = (byte)((prefix[i] & mask) | (rnd[i] & ~mask));
        }

        // Avoid the all-zero and ::1 host parts.
        var hostAllZero = true;
        for (var i = 8; i < 15; i++) if (bytes[i] != 0) hostAllZero = false;
        if (hostAllZero && bytes[15] <= 1) bytes[15] = 2;
        return new IPAddress(bytes);
    }

    // The user's current address, minting + persisting a random one on first use. `secret` is unused
    // (kept for call-site compatibility); the address is random, not derived.
    public async Task<IPAddress> AddrForUserAsync(string prefixCidr, string secret, string discordId, CancellationToken ct = default)
    {
        var row = await _db.CaptureProxyAddrs.AsNoTracking().FirstOrDefaultAsync(a => a.DiscordId == discordId, ct);
        if (row is not null) return IPAddress.Parse(row.Addr);
        return await MintAsync(prefixCidr, discordId, ct);
    }

    // Replace the user's address with a fresh random one, killing the old (leaked) address. Returns the
    // new address. The phone must be reconfigured with it; the old one immediately resolves to nobody.
    public async Task<IPAddress> RotateAsync(string prefixCidr, string discordId, CancellationToken ct = default)
        => await MintAsync(prefixCidr, discordId, ct);

    // Upsert a fresh random address for the user. Retries on the (astronomically unlikely) unique-index
    // collision with a new random value.
    private async Task<IPAddress> MintAsync(string prefixCidr, string discordId, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var addr = RandomInPrefix(prefixCidr);
            var canonical = addr.ToString();
            var row = await _db.CaptureProxyAddrs.FirstOrDefaultAsync(a => a.DiscordId == discordId, ct);
            if (row is null)
                _db.CaptureProxyAddrs.Add(new CaptureProxyAddr { DiscordId = discordId, Addr = canonical, CreatedAt = DateTimeOffset.UtcNow });
            else
                row.Addr = canonical;
            try { await _db.SaveChangesAsync(ct); return addr; }
            catch (DbUpdateException) { _db.ChangeTracker.Clear(); } // addr collision: retry with a new random
        }
        throw new InvalidOperationException("could not mint a unique capture address after retries");
    }

    // Reverse-map a destination address to a Discord id, or null (unissued / rotated-away address).
    public async Task<string?> UserForAddrAsync(IPAddress addr, CancellationToken ct = default)
    {
        var canonical = addr.ToString();
        return (await _db.CaptureProxyAddrs.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Addr == canonical, ct))?.DiscordId;
    }
}
