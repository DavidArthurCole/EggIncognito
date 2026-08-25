using EggIncognito.Services.Devices;

namespace EggIncognito.Tests.Devices;

public class DeviceClaimRegistryTests {
    [Fact]
    public void Claim_SetsHeldTrue_ReturnsNowPlusTtl() {
        var time = new TestTimeProvider { Now = DateTimeOffset.UtcNow };
        var registry = new DeviceClaimRegistry(time);

        var expires = registry.Claim("d1", TimeSpan.FromSeconds(60));

        Assert.True(registry.IsHeld("d1"));
        Assert.Equal(time.Now + TimeSpan.FromSeconds(60), expires);
    }

    [Fact]
    public void IsHeld_AfterTtlElapses_FalseAndCleansUp() {
        var time = new TestTimeProvider { Now = DateTimeOffset.UtcNow };
        var registry = new DeviceClaimRegistry(time);
        registry.Claim("d1", TimeSpan.FromSeconds(10));

        time.Now = time.Now.AddSeconds(11);

        Assert.False(registry.IsHeld("d1"));
        Assert.False(registry.IsHeld("d1"));
    }

    [Fact]
    public void Release_ClearsHeldImmediately() {
        var time = new TestTimeProvider { Now = DateTimeOffset.UtcNow };
        var registry = new DeviceClaimRegistry(time);
        registry.Claim("d1", TimeSpan.FromSeconds(60));

        registry.Release("d1");

        Assert.False(registry.IsHeld("d1"));
    }

    [Fact]
    public void Claim_CalledAgainBeforeExpiry_ExtendsExpiry() {
        var time = new TestTimeProvider { Now = DateTimeOffset.UtcNow };
        var registry = new DeviceClaimRegistry(time);
        registry.Claim("d1", TimeSpan.FromSeconds(10));

        time.Now = time.Now.AddSeconds(5);
        var expires = registry.Claim("d1", TimeSpan.FromSeconds(60));

        time.Now = time.Now.AddSeconds(10);

        Assert.True(registry.IsHeld("d1"));
        Assert.Equal(time.Now.AddSeconds(50), expires);
    }

    [Fact]
    public void IsHeld_UnknownId_False() {
        var registry = new DeviceClaimRegistry(new TestTimeProvider { Now = DateTimeOffset.UtcNow });

        Assert.False(registry.IsHeld("nope"));
    }

    private sealed class TestTimeProvider : TimeProvider {
        public DateTimeOffset Now { get; set; }
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
