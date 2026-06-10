using EggIncognito.Capture;

namespace EggIncognito.Tests;

public class CaptureHubKnownDevicesTests
{
    private static RememberedDevice Known(string ip) =>
        new(ip, "phone.local", "iOS", "1.35.6", "09:00:00", "09:30:00", 5);

    [Fact]
    public void SeededKnownDevice_AppearsOfflineUntilItConnects()
    {
        var hub = new CaptureHub();
        hub.SeedKnownDevices([Known("192.168.1.5")]);

        var d = Assert.Single(hub.StatsSnapshot().Devices);
        Assert.Equal("192.168.1.5", d.Ip);
        Assert.False(d.Online);
        Assert.Equal("iOS", d.Os);
        Assert.Equal(5, d.TotalConnections);
    }

    [Fact]
    public void Connect_FlipsKnownDeviceOnline_AndBumpsLifetimeCount()
    {
        var hub = new CaptureHub();
        hub.SeedKnownDevices([Known("192.168.1.5")]);

        hub.RecordConnection(1, "192.168.1.5", "10:00:00");

        var d = Assert.Single(hub.StatsSnapshot().Devices);
        Assert.True(d.Online);
        Assert.Equal(6, d.TotalConnections);          // 5 remembered + 1 new
        Assert.Equal("09:00:00", d.FirstSeen);        // adopted the remembered first-seen
        Assert.Equal("iOS", d.Os);                    // adopted the remembered OS
    }

    [Fact]
    public void Disconnect_AllGone_MarksDeviceOffline()
    {
        var hub = new CaptureHub();
        hub.RecordConnection(1, "192.168.1.7", "10:00:00");
        Assert.True(Assert.Single(hub.StatsSnapshot().Devices).Online);

        hub.RecordDisconnection(0, "10:05:00");
        Assert.False(Assert.Single(hub.StatsSnapshot().Devices).Online);
    }

    [Fact]
    public void Snapshot_MergesLiveAndRememberedOnly()
    {
        var hub = new CaptureHub();
        hub.SeedKnownDevices([Known("192.168.1.5"), Known("192.168.1.6")]);
        hub.RecordConnection(1, "192.168.1.5", "10:00:00"); // 5 goes live; 6 stays remembered-only

        var snap = hub.SnapshotRememberedDevices();
        Assert.Equal(2, snap.Count);
        Assert.Contains(snap, d => d.Ip == "192.168.1.5" && d.TotalConnections == 6);
        Assert.Contains(snap, d => d.Ip == "192.168.1.6" && d.TotalConnections == 5);
    }

    [Fact]
    public void DevicesChanged_FiresOnConnect()
    {
        var hub = new CaptureHub();
        var fired = 0;
        hub.DevicesChanged = () => fired++;
        hub.RecordConnection(1, "192.168.1.5", "10:00:00");
        Assert.True(fired > 0);
    }
}
