using System.Net;
using EggIncognito.Data.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EggIncognito.Tests;

public class CaptureAddressStoreTests
{
    const string Prefix = "2a01:4f8:c012:e15b::/64";
    const string Secret = "test-secret-key";

    [Fact]
    public void Derive_IsDeterministic()
    {
        var a = CaptureAddressStore.Derive(Prefix, Secret, "123");
        var b = CaptureAddressStore.Derive(Prefix, Secret, "123");
        Assert.Equal(a, b);
    }

    [Fact]
    public void Derive_HonorsSubPrefixUpperHalf()
    {
        // A /65 upper-half sub-prefix: the first host bit (bit 64) must stay 1, and the first 64
        // prefix bits must be preserved. Several ids must all land in the upper half.
        const string sub = "2a01:4f8:c012:e15b:8000::/65";
        var prefixBytes = IPAddress.Parse("2a01:4f8:c012:e15b::").GetAddressBytes();
        foreach (var id in new[] { "1", "2", "999", "123456789012345678" })
        {
            var bytes = CaptureAddressStore.Derive(sub, Secret, id).GetAddressBytes();
            for (var i = 0; i < 8; i++) Assert.Equal(prefixBytes[i], bytes[i]); // /64 prefix intact
            Assert.Equal(0x80, bytes[8] & 0x80);                                // bit 64 set (upper half)
        }
    }

    [Fact]
    public void Derive_DiffersPerUser()
    {
        var a = CaptureAddressStore.Derive(Prefix, Secret, "123");
        var b = CaptureAddressStore.Derive(Prefix, Secret, "456");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Derive_IsInPrefix()
    {
        var a = CaptureAddressStore.Derive(Prefix, Secret, "123");
        var prefixAddr = IPAddress.Parse("2a01:4f8:c012:e15b::");
        var ab = a.GetAddressBytes();
        var pb = prefixAddr.GetAddressBytes();
        for (var i = 0; i < 8; i++) Assert.Equal(pb[i], ab[i]);
    }

    [Fact]
    public void Derive_AvoidsReservedHostPart()
    {
        var a = CaptureAddressStore.Derive(Prefix, Secret, "123");
        var bytes = a.GetAddressBytes();
        var hostAllZeroExceptLast = true;
        for (var i = 8; i < 15; i++) if (bytes[i] != 0) hostAllZeroExceptLast = false;
        Assert.False(hostAllZeroExceptLast && bytes[15] <= 1);
    }

    // Persistence round-trip. The test project carries no EF test provider (tests-DB-free repo rule:
    // no InMemory/Testcontainers/SkippableFact deps), so a real Postgres round-trip cannot run here.
    // Run manually against a live DB if AddrForUserAsync/UserForAddrAsync change.
    [Fact(Skip = "requires Postgres; no EF test provider per tests-DB-free repo rule")]
    public async Task AddrForUser_PersistsAndReverseMaps()
    {
        var opts = new DbContextOptionsBuilder<EggIncognitoDbContext>()
            .UseNpgsql("Host=frame;Port=5432;Database=eggincognito_test;Username=ei;Password=ei").Options;
        await using var db = new EggIncognitoDbContext(opts);
        var store = new CaptureAddressStore(db);

        var addr = await store.AddrForUserAsync(Prefix, Secret, "123");
        var who = await store.UserForAddrAsync(addr);
        Assert.Equal("123", who);
    }
}
