using System.Net;
using System.Security.Cryptography;
using System.Text;
using EggIncognito.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Data.Services;

// Issues + reverse-maps per-user IPv6 proxy addresses. The address is HMAC-derived from the Discord
// id over a server secret, so it is deterministic, stable, and unguessable without the secret. The
// front door identifies a user by the destination address of the connection (see the design spec).
public sealed class CaptureAddressStore(EggIncognitoDbContext db)
{
    readonly EggIncognitoDbContext _db = db;

    // Pure derivation: the prefix bits (per the CIDR length) are fixed; the remaining host bits come
    // from HMAC-SHA256(secret, id). Honors any prefix length 0..128 (e.g. a /65 sub-prefix that shares
    // the /64 with the host's own addresses). Reserved host parts (all-zero, ::1) are bumped to ::2.
    public static IPAddress Derive(string prefixCidr, string secret, string discordId)
    {
        var parts = prefixCidr.Split('/');
        var prefix = IPAddress.Parse(parts[0]).GetAddressBytes(); // 16 bytes
        var prefixLen = parts.Length > 1 ? int.Parse(parts[1]) : 64;
        var mac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(discordId));

        var bytes = new byte[16];
        for (var i = 0; i < 16; i++)
        {
            // Bit position of this byte's high bit, measured from the front of the address.
            var firstBit = i * 8;
            if (firstBit + 8 <= prefixLen) { bytes[i] = prefix[i]; continue; }   // fully inside the prefix
            if (firstBit >= prefixLen) { bytes[i] = mac[i]; continue; }          // fully a host byte
            // Straddling byte: high (prefixLen - firstBit) bits from the prefix, the rest from the mac.
            var prefixBits = prefixLen - firstBit;
            var mask = (byte)(0xFF << (8 - prefixBits));
            bytes[i] = (byte)((prefix[i] & mask) | (mac[i] & ~mask));
        }

        // Avoid the all-zero and ::1 host parts.
        var hostAllZero = true;
        for (var i = 8; i < 15; i++) if (bytes[i] != 0) hostAllZero = false;
        if (hostAllZero && bytes[15] <= 1) bytes[15] = 2;
        return new IPAddress(bytes);
    }

    // Derive (if needed), persist on first use, return the user's address.
    public async Task<IPAddress> AddrForUserAsync(string prefixCidr, string secret, string discordId, CancellationToken ct = default)
    {
        var derived = Derive(prefixCidr, secret, discordId);
        var canonical = derived.ToString();
        var row = await _db.CaptureProxyAddrs.FirstOrDefaultAsync(a => a.DiscordId == discordId, ct);
        if (row is null)
        {
            _db.CaptureProxyAddrs.Add(new CaptureProxyAddr { DiscordId = discordId, Addr = canonical, CreatedAt = DateTimeOffset.UtcNow });
            try { await _db.SaveChangesAsync(ct); }
            catch (DbUpdateException)
            {
                // Lost a race (same user, concurrent first-use) or an address collision. Re-read; if the
                // user now has a row, use it. A genuine cross-user address collision (64-bit HMAC) is
                // astronomically unlikely and rethrows.
                _db.ChangeTracker.Clear();
                var existing = await _db.CaptureProxyAddrs.AsNoTracking().FirstOrDefaultAsync(a => a.DiscordId == discordId, ct);
                if (existing is null) throw;
            }
        }
        else if (row.Addr != canonical)
        {
            row.Addr = canonical;
            await _db.SaveChangesAsync(ct);
        }
        return derived;
    }

    // Reverse-map a destination address to a Discord id, or null.
    public async Task<string?> UserForAddrAsync(IPAddress addr, CancellationToken ct = default)
    {
        var canonical = addr.ToString();
        return (await _db.CaptureProxyAddrs.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Addr == canonical, ct))?.DiscordId;
    }
}
