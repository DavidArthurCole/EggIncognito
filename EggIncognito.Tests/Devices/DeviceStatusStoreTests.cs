using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EggIncognito.Tests.Devices;

public class DeviceStatusStoreTests
{
    // Persistence round-trip. The test project carries no EF test provider (tests-DB-free repo rule:
    // no InMemory/Testcontainers/SkippableFact deps), so a real Postgres round-trip cannot run here.
    // Run manually against a live DB if DeviceStatusStore logic changes.

    [Fact(Skip = "requires Postgres; no EF test provider per tests-DB-free repo rule")]
    public async Task Upsert_ThenEnabledDevices_ReturnsRow()
    {
        var opts = new DbContextOptionsBuilder<EggIncognitoDbContext>()
            .UseNpgsql("Host=frame;Port=5432;Database=eggincognito_test;Username=ei;Password=ei").Options;
        await using var db = new EggIncognitoDbContext(opts);
        var store = new DeviceStatusStore(db);
        await store.UpsertDeviceAsync("frame-android", "android", "A15", "RF8X20GLYDY", "com.auxbrain.egginc", default);
        var list = await store.EnabledDevicesAsync(default);
        Assert.Single(list);
        Assert.Equal("RF8X20GLYDY", list[0].Target);
    }

    [Fact(Skip = "requires Postgres; no EF test provider per tests-DB-free repo rule")]
    public async Task RecordProbe_LatestPerDevice_ReturnsMostRecent()
    {
        var opts = new DbContextOptionsBuilder<EggIncognitoDbContext>()
            .UseNpgsql("Host=frame;Port=5432;Database=eggincognito_test;Username=ei;Password=ei").Options;
        await using var db = new EggIncognitoDbContext(opts);
        var store = new DeviceStatusStore(db);
        await store.UpsertDeviceAsync("d1", "android", "A15", "t", "p", default);
        await store.RecordProbeAsync(new DeviceProbe
        {
            DeviceId = "d1", ProbedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            Reachable = true, InstalledAppVersion = "1.35.6", Result = "no_change",
        }, default);
        await store.RecordProbeAsync(new DeviceProbe
        {
            DeviceId = "d1", ProbedAt = DateTimeOffset.UtcNow,
            Reachable = true, InstalledAppVersion = "1.35.7", Result = "new_version",
        }, default);

        var latest = await store.LatestPerDeviceAsync(default);
        Assert.Single(latest);
        Assert.Equal("1.35.7", latest[0].InstalledAppVersion);
        Assert.Equal("new_version", latest[0].Result);

        var hist = await store.HistoryAsync("d1", 10, default);
        Assert.Equal(2, hist.Count);
        Assert.Equal("1.35.7", hist[0].InstalledAppVersion); // newest first
    }
}
