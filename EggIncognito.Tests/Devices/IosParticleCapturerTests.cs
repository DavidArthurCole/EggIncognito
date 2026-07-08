using EggIncognito.Core.Services.Devices;
using EggIncognito.Services.Devices;
using Xunit;

namespace EggIncognito.Tests.Devices;

public class IosParticleCapturerTests
{
    sealed class FakeRunner(Func<string, string[], ProcessResult> fn) : IProcessRunner
    {
        public readonly List<(string exe, string[] args)> Calls = [];
        public Task<ProcessResult> RunAsync(string exe, string[] args, CancellationToken ct)
        {
            Calls.Add((exe, args));
            return Task.FromResult(fn(exe, args));
        }
    }

    static string TempScript()
    {
        var p = Path.Combine(Path.GetTempPath(), $"egi-frida-{Guid.NewGuid():N}.js");
        File.WriteAllText(p, "// fake hook");
        return p;
    }

    [Fact]
    public async Task Capture_MissingScript_ReturnsNull()
    {
        var runner = new FakeRunner((_, _) => new ProcessResult(0, "", ""));
        var cap = new IosParticleCapturer(runner, "h", "2222", "/key", "/does/not/exist.js");
        Assert.Null(await cap.CaptureAsync(default));
    }

    [Fact]
    public async Task Capture_PushFails_ReturnsNull()
    {
        var script = TempScript();
        try
        {
            var runner = new FakeRunner((exe, _) => exe == "scp"
                ? new ProcessResult(1, "", "scp: permission denied")
                : new ProcessResult(0, "", ""));
            var cap = new IosParticleCapturer(runner, "h", "2222", "/key", script);
            Assert.Null(await cap.CaptureAsync(default));
        }
        finally { File.Delete(script); }
    }

    [Fact]
    public async Task Capture_NoLogPulled_ReturnsNotOkWithFridaDiag()
    {
        var script = TempScript();
        try
        {
            int scpCount = 0;
            var runner = new FakeRunner((exe, args) =>
            {
                if (exe == "scp")
                {
                    scpCount++;
                    return scpCount == 1 ? new ProcessResult(0, "", "") : new ProcessResult(1, "", "scp: no such file");
                }
                return new ProcessResult(0, "addParticle symbol not resolved", "");
            });
            var cap = new IosParticleCapturer(runner, "h", "2222", "/key", script);
            var m = await cap.CaptureAsync(default);
            Assert.NotNull(m);
            Assert.False(m!.Value.Ok);
            Assert.Contains("symbol not resolved", m.Value.Diagnostics);
        }
        finally { File.Delete(script); }
    }

    [Fact]
    public async Task Capture_InjectsAddrOffsetIntoStagedScript()
    {
        var script = TempScript();
        try
        {
            string? pushedContent = null;
            int scpCount = 0;
            var runner = new FakeRunner((exe, args) =>
            {
                if (exe == "scp")
                {
                    scpCount++;
                    if (scpCount == 1) pushedContent = File.ReadAllText(args[^2]);
                    return new ProcessResult(0, "", "");
                }
                return new ProcessResult(0, "", "");
            });
            var cap = new IosParticleCapturer(runner, "h", "2222", "/key", script, "0x1234abc");
            await cap.CaptureAsync(default);

            Assert.NotNull(pushedContent);
            Assert.Contains("const addrOffset = '0x1234abc';", pushedContent);
            Assert.Contains("// fake hook", pushedContent);
        }
        finally { File.Delete(script); }
    }

    [Fact]
    public async Task Capture_Success_PullsAndParses()
    {
        var script = TempScript();
        try
        {
            var ndjson = "{\"t\":0,\"mesh\":\"0xA\",\"x\":[1,0,0,0,1,0,0,0,1,4,5,6],\"s\":0.5}";
            int scpCount = 0;
            var runner = new FakeRunner((exe, args) =>
            {
                if (exe == "scp")
                {
                    scpCount++;
                    if (scpCount == 2) File.WriteAllText(args[^1], ndjson);
                    return new ProcessResult(0, "", "");
                }
                return new ProcessResult(0, "__frida_exit_0", "");
            });
            var cap = new IosParticleCapturer(runner, "phone", "2222", "/key", script);
            var m = await cap.CaptureAsync(default);

            Assert.NotNull(m);
            Assert.True(m!.Value.Ok);
            Assert.Equal(1, m.Value.TotalSamples);
            Assert.Equal("0xA", m.Value.Dominant!.Value.Mesh);
            Assert.Equal(4f, m.Value.Dominant!.Value.Centroid[0], 3);
            var push = runner.Calls.First(c => c.exe == "scp");
            Assert.Contains(push.args, a => a.StartsWith("root@phone:"));
            Assert.EndsWith(".js", push.args[^2]);
        }
        finally { File.Delete(script); }
    }
}
