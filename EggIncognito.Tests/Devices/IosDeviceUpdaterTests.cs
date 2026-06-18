using EggIncognito.Core.Services.Devices;
using EggIncognito.Data.Models;
using EggIncognito.Services.Devices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EggIncognito.Tests.Devices;

public class IosDeviceUpdaterTests
{
    sealed class FakeRunner(Func<string[], ProcessResult> fn) : IProcessRunner
    {
        public int TouchCalls;
        public Task<ProcessResult> RunAsync(string exe, string[] args, CancellationToken ct)
        {
            if (IsTouch(args)) TouchCalls++;
            return Task.FromResult(fn(args));
        }
        // the ssh touch command is one combined arg: "touch /var/mobile/eggupdate.trigger"
        public static bool IsTouch(string[] args) => args.Any(a => a.StartsWith("touch ", StringComparison.Ordinal));
    }

    static Device Dev => new() { Id = "i", Platform = "ios", Target = "UDID123", Package = "com.auxbrain.egginc" };

    // ideviceinstaller -l -o xml output: a plist with one app dict carrying the bundle id + version.
    static string Plist(string version) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <plist version="1.0"><array><dict>
          <key>CFBundleIdentifier</key><string>com.auxbrain.egginc</string>
          <key>CFBundleShortVersionString</key><string>{version}</string>
        </dict></array></plist>
        """;

    // Sequences installed-version reads (ideviceinstaller) by call index; ssh touch returns the given exit.
    static FakeRunner SeqRunner(string[] versionsByRead, int touchExit, string touchErr = "")
    {
        var reads = 0;
        return new FakeRunner(args =>
        {
            if (FakeRunner.IsTouch(args))
                return new ProcessResult(touchExit, "", touchErr);
            if (args.Contains("-l")) // ideviceinstaller list
            {
                var i = Math.Min(reads, versionsByRead.Length - 1);
                reads++;
                return new ProcessResult(0, Plist(versionsByRead[i]), "");
            }
            return new ProcessResult(0, "", "");
        });
    }

    static IConfiguration Config(bool configured) =>
        new ConfigurationBuilder().AddInMemoryCollection(configured
            ? new Dictionary<string, string?>
            {
                ["DeviceUpdate:Ios:SshHost"] = "192.168.1.132",
                ["DeviceUpdate:Ios:SshKeyPath"] = "/home/david/.ssh/id_phone_ed25519",
                ["DeviceUpdate:Ios:PollSeconds"] = "0", // no real delay in tests
                ["DeviceUpdate:Ios:PollAttempts"] = "3",
            }
            : []).Build();

    static IosDeviceUpdater Make(IProcessRunner runner, bool configured = true) =>
        new(runner, Config(configured), NullLogger<IosDeviceUpdater>.Instance);

    [Fact]
    public async Task Update_Verified_WhenVersionClimbs()
    {
        // read#1 = before (1.35.8); touch fires; read#2 = still old; read#3 = climbed to 1.36.
        var runner = SeqRunner(["1.35.8", "1.35.8", "1.36.0.2"], touchExit: 0);
        var o = await Make(runner).UpdateAsync(Dev, "1.36.0.2", default);
        Assert.True(o.Started);
        Assert.True(o.Verified);
        Assert.Equal("1.36.0.2", o.ToVersion);
        Assert.Equal(1, runner.TouchCalls);
    }

    [Fact]
    public async Task Update_NoOp_WhenAlreadyCurrent()
    {
        var runner = SeqRunner(["1.36.0.2"], touchExit: 0);
        var o = await Make(runner).UpdateAsync(Dev, "1.36.0.2", default);
        Assert.False(o.Started);
        Assert.True(o.Verified);
        Assert.Equal("already current", o.Note);
        Assert.Equal(0, runner.TouchCalls); // never fired the trigger
    }

    [Fact]
    public async Task Update_NotConfigured_WhenSshUnset()
    {
        var runner = SeqRunner(["1.35.8"], touchExit: 0);
        var o = await Make(runner, configured: false).UpdateAsync(Dev, "1.36.0.2", default);
        Assert.False(o.Started);
        Assert.False(o.Verified);
        Assert.Contains("not configured", o.Note);
        Assert.Equal(0, runner.TouchCalls);
    }

    [Fact]
    public async Task Update_TriggerFails_NotStarted()
    {
        var runner = SeqRunner(["1.35.8"], touchExit: 255, touchErr: "Permission denied");
        var o = await Make(runner).UpdateAsync(Dev, "1.36.0.2", default);
        Assert.False(o.Started);
        Assert.False(o.Verified);
        Assert.Contains("trigger ssh failed", o.Note);
    }

    [Fact]
    public async Task Update_VersionStuck_StartedNotVerified()
    {
        // trigger fires but the version never climbs within the poll window.
        var runner = SeqRunner(["1.35.8"], touchExit: 0);
        var o = await Make(runner).UpdateAsync(Dev, "1.36.0.2", default);
        Assert.True(o.Started);
        Assert.False(o.Verified);
        Assert.Equal("1.35.8", o.ToVersion);
        Assert.Equal(1, runner.TouchCalls);
    }
}
