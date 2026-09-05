using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Tests.Devices;

public class DeviceRootTests {
    [Fact]
    public void WrapMountMaster_MagiskSu_UsesMountMasterFlag() {
        var root = new RootAccess(true, "/sbin/su", "magisk", MountMaster: true);
        Assert.Equal("/sbin/su -mm -c 'ls /system'", root.WrapMountMaster("ls /system"));
        Assert.Equal("/sbin/su -c 'ls /system'", root.Wrap("ls /system"));
    }

    [Fact]
    public void WrapMountMaster_PlainSu_FallsBackToWrap() {
        var root = new RootAccess(true, "/system/bin/su", "plain");
        Assert.Equal(root.Wrap("id"), root.WrapMountMaster("id"));
        Assert.DoesNotContain("-mm", root.WrapMountMaster("id"));
    }

    [Fact]
    public void WrapMountMaster_RootShell_PassesCommandThrough() {
        var root = new RootAccess(true, null, "adb shell is uid=0", MountMaster: true);
        Assert.Equal("id", root.WrapMountMaster("id"));
    }

    [Fact]
    public void Wrap_QuotesSingleQuotesInsideTheCommand() {
        var root = new RootAccess(true, "/sbin/su", "magisk", MountMaster: true);
        Assert.Equal("/sbin/su -mm -c 'echo '\\''hi'\\'''", root.WrapMountMaster("echo 'hi'"));
    }

    [Fact]
    public async Task Probe_SkipsStockSu_AndAcceptsMagiskSu() {
        var conn = new ScriptedConnection {
            ["id -u"] = new ProcessResult(0, "2000\n", ""),
            ["/sbin/su -c id"] = new ProcessResult(0, "uid=0(root) gid=0(root)\n", "")
        };

        var root = await DeviceRoot.ProbeAsync(conn, default);

        Assert.True(root.Ok);
        Assert.Equal("/sbin/su", root.SuBinary);
        Assert.True(root.MountMaster);
        Assert.Contains("/sbin/su", root.Detail);
    }

    [Fact]
    public async Task Probe_StockSuOutput_IsNotRoot_EvenWhenItEchoesUid0() {
        var conn = new ScriptedConnection {
            ["id -u"] = new ProcessResult(0, "2000\n", ""),
            ["/sbin/su -c id"] = new ProcessResult(1, "", "Permission denied"),
            ["/debug_ramdisk/su -c id"] = new ProcessResult(127, "", "not found"),
            ["su -c id"] = new ProcessResult(1, "uid=0 usage: su [WHO [COMMAND...]]", "su: invalid uid/gid '-c'"),
            ["/system/bin/su -c id"] = new ProcessResult(127, "", "not found")
        };

        var root = await DeviceRoot.ProbeAsync(conn, default);

        Assert.False(root.Ok);
        Assert.Null(root.SuBinary);
    }

    [Fact]
    public async Task Probe_NonMagiskSu_ReportsNoMountMaster() {
        var conn = new ScriptedConnection {
            ["id -u"] = new ProcessResult(0, "2000\n", ""),
            ["/sbin/su -c id"] = new ProcessResult(127, "", "not found"),
            ["/debug_ramdisk/su -c id"] = new ProcessResult(127, "", "not found"),
            ["su -c id"] = new ProcessResult(0, "uid=0(root)\n", ""),
            ["su -v 2>&1; su --version 2>&1"] = new ProcessResult(0, "1.0 supersu\n", "")
        };

        var root = await DeviceRoot.ProbeAsync(conn, default);

        Assert.True(root.Ok);
        Assert.Equal("su", root.SuBinary);
        Assert.False(root.MountMaster);
    }

    [Fact]
    public async Task Probe_RootShell_NeedsNoSu() {
        var conn = new ScriptedConnection { ["id -u"] = new ProcessResult(0, "0\n", "") };

        var root = await DeviceRoot.ProbeAsync(conn, default);

        Assert.True(root.Ok);
        Assert.Null(root.SuBinary);
        Assert.Single(conn.Commands);
    }

    private sealed class ScriptedConnection : IDeviceConnection {
        private readonly Dictionary<string, ProcessResult> _replies = [with(StringComparer.Ordinal)];
        public readonly List<string> Commands = [];

        public ProcessResult this[string command] {
            set => _replies[command] = value;
        }

        public string Platform => "android";

        public Task<ProcessResult> ShellAsync(string command, CancellationToken ct) {
            Commands.Add(command);
            return Task.FromResult(_replies.TryGetValue(command, out var r) ? r : new ProcessResult(127, "", "not found"));
        }

        public Task<byte[]?> PullBytesAsync(string remotePath, CancellationToken ct) => Task.FromResult<byte[]?>(null);
        public Task<bool> PushFileAsync(string localPath, string remotePath, CancellationToken ct) => Task.FromResult(false);
    }
}
