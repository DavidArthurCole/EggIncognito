using EggIncognito.Core.Models;
using EggIncognito.Runner.Adb;
using EggIncognito.Runner.Extract;
using EggIncognito.Runner.Runners;
using EggIncognito.Runner.State;
using Xunit;

namespace EggIncognito.Runner.Tests;

public class AndroidRunnerTests
{
    private sealed class FakeAdb : IAdbClient
    {
        public string Dumpsys = "versionCode=111343\nversionName=1.35.7\n";
        public string DumpsysPackage(string package) => Dumpsys;
        public string PullArmApk(string package, string destPath)
        {
            File.WriteAllText(destPath, "apk");
            return destPath;
        }
    }

    private sealed class FakeExtractor : IProtoExtractor
    {
        public byte[] Bytes = System.Text.Encoding.UTF8.GetBytes("syntax = \"proto2\";\npackage ei;\n");
        public byte[] Extract(string apkPath) => Bytes;
    }

    private static AndroidRunner Make(FakeAdb adb, VersionState state, out List<NewVersionEvent> sent)
    {
        var captured = new List<NewVersionEvent>();
        sent = captured;
        var cvState = new ClientVersionState(Path.Combine(Path.GetTempPath(), $"cv-{Guid.NewGuid():N}"), null);
        return new AndroidRunner(adb, new FakeExtractor(), state, new NullClientVersionReader(), cvState,
            "com.auxbrain.egginc", Path.GetTempPath(), evt => captured.Add(evt));
    }

    private static VersionState FreshState() =>
        new(Path.Combine(Path.GetTempPath(), $"st-{Guid.NewGuid():N}"));

    [Fact]
    public void NewBuild_Emits_AndSavesState()
    {
        var runner = Make(new FakeAdb(), FreshState(), out var sent);
        var outcome = runner.RunOnce(force: false);
        Assert.True(outcome.Emitted);
        Assert.Equal("111343", outcome.Build);
        var evt = Assert.Single(sent);
        Assert.Equal("1.35.7", evt.AppVersion);
        Assert.Equal("111343", evt.Build);
        Assert.Equal("android", evt.Platform);
        Assert.False(string.IsNullOrEmpty(evt.ProtoSha));
        Assert.False(string.IsNullOrEmpty(evt.ProtoTextB64));
        Assert.Null(evt.ClientVersion);
    }

    [Fact]
    public void SameBuild_NoForce_DoesNotEmit()
    {
        var state = FreshState();
        var runner = Make(new FakeAdb(), state, out var sent);
        runner.RunOnce(force: false);
        sent.Clear();
        var outcome = runner.RunOnce(force: false);
        Assert.False(outcome.Emitted);
        Assert.Empty(sent);
    }

    [Fact]
    public void SameBuild_Force_EmitsAnyway()
    {
        var state = FreshState();
        var runner = Make(new FakeAdb(), state, out var sent);
        runner.RunOnce(force: false);
        sent.Clear();
        var outcome = runner.RunOnce(force: true);
        Assert.True(outcome.Emitted);
        Assert.Single(sent);
    }
}
