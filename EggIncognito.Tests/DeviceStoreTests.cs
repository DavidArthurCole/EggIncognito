using EggIncognito.Capture;

namespace EggIncognito.Tests;

public class DeviceStoreTests
{
    private static string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "egi-dev-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    [Fact]
    public void Load_MissingFile_ReturnsEmpty()
    {
        Assert.Empty(new DeviceStore(TempDir()).Load());
    }

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var dir = TempDir();
        var store = new DeviceStore(dir);
        var devices = new[]
        {
            new RememberedDevice("192.168.1.5", "phone.local", "iOS", "1.35.6", "10:00:00", "10:05:00", 7),
            new RememberedDevice("192.168.1.6", null, "Android", null, "11:00:00", "11:01:00", 2),
        };
        store.Save(devices);

        var loaded = new DeviceStore(dir).Load();
        Assert.Equal(2, loaded.Count);
        var d5 = Assert.Single(loaded, d => d.Ip == "192.168.1.5");
        Assert.Equal("iOS", d5.Os);
        Assert.Equal("1.35.6", d5.GameVersion);
        Assert.Equal(7, d5.TotalConnections);
        Assert.Equal("phone.local", d5.Hostname);
    }

    [Fact]
    public void Save_CapsAtMostRecentByLastSeen()
    {
        var dir = TempDir();
        var devices = Enumerable.Range(1, 60)
            .Select(i => new RememberedDevice($"10.0.0.{i}", null, null, null, "t", $"{i:D4}", 1))
            .ToArray();
        new DeviceStore(dir).Save(devices);

        var loaded = new DeviceStore(dir).Load();
        Assert.Equal(50, loaded.Count);
        Assert.Contains(loaded, d => d.LastSeen == "0060");
        Assert.DoesNotContain(loaded, d => d.LastSeen == "0001");
    }

    [Fact]
    public void Load_CorruptFile_ReturnsEmpty()
    {
        var dir = TempDir();
        File.WriteAllText(Path.Combine(dir, "devices.json"), "{ not json");
        Assert.Empty(new DeviceStore(dir).Load());
    }
}
