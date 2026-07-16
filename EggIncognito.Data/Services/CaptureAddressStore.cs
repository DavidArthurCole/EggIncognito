using System.Net;
using System.Security.Cryptography;
using System.Text;
using EggIncognito.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Data.Services;

public sealed class CaptureAddressStore(EggIncognitoDbContext db)
{
    public static IPAddress RandomInPrefix(string prefixCidr)
    {
        var parts = prefixCidr.Split('/');
        var prefix = IPAddress.Parse(parts[0]).GetAddressBytes();
        var prefixLen = parts.Length > 1 ? int.Parse(parts[1]) : 64;
        var rnd = RandomNumberGenerator.GetBytes(16);

        var bytes = new byte[16];
        for (var i = 0; i < 16; i++)
        {
            var firstBit = i * 8;
            if (firstBit + 8 <= prefixLen) { bytes[i] = prefix[i]; continue; }
            if (firstBit >= prefixLen) { bytes[i] = rnd[i]; continue; }
            var prefixBits = prefixLen - firstBit;
            var mask = (byte)(0xFF << (8 - prefixBits));
            bytes[i] = (byte)((prefix[i] & mask) | (rnd[i] & ~mask));
        }

        var hostAllZero = true;
        for (var i = 8; i < 15; i++) if (bytes[i] != 0) hostAllZero = false;
        if (hostAllZero && bytes[15] <= 1) bytes[15] = 2;
        return new IPAddress(bytes);
    }

    public async Task<IPAddress> AddrForUserAsync(string prefixCidr, string secret, Guid userId, CancellationToken ct = default)
    {
        var row = await db.CaptureProxyAddrs.AsNoTracking().FirstOrDefaultAsync(a => a.UserId == userId, ct);
        if (row is not null) return IPAddress.Parse(row.Addr);
        return await MintAsync(prefixCidr, userId, ct);
    }

   
    public async Task<IPAddress> RotateAsync(string prefixCidr, Guid userId, CancellationToken ct = default)
        => await MintAsync(prefixCidr, userId, ct);

    private async Task<IPAddress> MintAsync(string prefixCidr, Guid userId, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var addr = RandomInPrefix(prefixCidr);
            var canonical = addr.ToString();
            var row = await db.CaptureProxyAddrs.FirstOrDefaultAsync(a => a.UserId == userId, ct);
            if (row is null)
                db.CaptureProxyAddrs.Add(new CaptureProxyAddr { UserId = userId, Addr = canonical, CreatedAt = DateTimeOffset.UtcNow });
            else
                row.Addr = canonical;
            try { await db.SaveChangesAsync(ct); return addr; }
            catch (DbUpdateException) { db.ChangeTracker.Clear(); }
        }
        throw new InvalidOperationException("could not mint a unique capture address after retries");
    }

    public async Task<Guid?> UserForAddrAsync(IPAddress addr, CancellationToken ct = default)
    {
        var canonical = addr.ToString();
        var row = await db.CaptureProxyAddrs.AsNoTracking().FirstOrDefaultAsync(a => a.Addr == canonical, ct);
        return row?.UserId;
    }
}
