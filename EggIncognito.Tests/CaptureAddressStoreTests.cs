using System.Net;
using EggIncognito.Data.Services;
using Xunit;

namespace EggIncognito.Tests;

public class CaptureAddressStoreTests {
    private const string Prefix = "2a01:4f8:c012:e15b::/64";

    [Fact]
    public void RandomInPrefix_IsRandom_NotDeterministic() {
        var a = CaptureAddressStore.RandomInPrefix(Prefix);
        var b = CaptureAddressStore.RandomInPrefix(Prefix);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void RandomInPrefix_HonorsSubPrefixUpperHalf() {
        const string sub = "2a01:4f8:c012:e15b:8000::/65";
        var prefixBytes = IPAddress.Parse("2a01:4f8:c012:e15b::").GetAddressBytes();
        for (var n = 0; n < 50; n++) {
            var bytes = CaptureAddressStore.RandomInPrefix(sub).GetAddressBytes();
            for (var i = 0; i < 8; i++) Assert.Equal(prefixBytes[i], bytes[i]);
            Assert.Equal(0x80, bytes[8] & 0x80);
        }
    }

    [Fact]
    public void RandomInPrefix_IsInPrefix() {
        var prefixAddr = IPAddress.Parse("2a01:4f8:c012:e15b::").GetAddressBytes();
        for (var n = 0; n < 50; n++) {
            var ab = CaptureAddressStore.RandomInPrefix(Prefix).GetAddressBytes();
            for (var i = 0; i < 8; i++) Assert.Equal(prefixAddr[i], ab[i]);
        }
    }

    [Fact]
    public void RandomInPrefix_AvoidsReservedHostPart() {
        for (var n = 0; n < 50; n++) {
            var bytes = CaptureAddressStore.RandomInPrefix(Prefix).GetAddressBytes();
            var hostAllZeroExceptLast = true;
            for (var i = 8; i < 15; i++) if (bytes[i] != 0) hostAllZeroExceptLast = false;
            Assert.False(hostAllZeroExceptLast && bytes[15] <= 1);
        }
    }
}
