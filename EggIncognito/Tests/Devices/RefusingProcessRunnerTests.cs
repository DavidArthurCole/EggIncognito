using EggIncognito.Core.Services.Devices;
using EggIncognito.Services.Devices.Fake;

namespace EggIncognito.Tests.Devices;

public class RefusingProcessRunnerTests {
    private static readonly Dictionary<string, string> Nothing = [];

    [Theory]
    [InlineData("adb")]
    [InlineData("ssh")]
    [InlineData("scp")]
    [InlineData("frida")]
    public async Task EveryCallIsRefusedAndNamesTheExecutable(string exe) {
        var runner = new RefusingProcessRunner();
        var result = await runner.RunAsync(exe, ["-s", "device"], CancellationToken.None);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Empty(result.Stdout);
        Assert.Contains(exe, result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AttemptsAreRecordedInOrder() {
        var runner = new RefusingProcessRunner();
        await runner.RunAsync("adb", [], CancellationToken.None);
        await runner.RunAsync("ssh", [], CancellationToken.None);
        Assert.Equal(new[] { "adb", "ssh" }, runner.Attempts);
    }

    [Fact]
    public void NothingIsAttemptedBeforeTheFirstCall() =>
        Assert.Empty(new RefusingProcessRunner().Attempts);

    [Fact]
    public async Task AFullFakeHarvestCompletesWithNoTransportInTheGraph() {
        foreach (var stack in new[] { FakeStack.Ios(), FakeStack.Android() }) {
            var probe = await stack.Platform.ProbeAsync(stack.Target, CancellationToken.None);
            Assert.True(probe.Reachable);

            foreach (var entry in stack.Platform.Manifest()) {
                await stack.Platform.FingerprintAsync(stack.Target, entry, CancellationToken.None);
                var batch = await stack.Platform.HarvestAsync(stack.Target, entry, Nothing, CancellationToken.None);
                Assert.True(batch.Ok || !entry.Supported);
            }

            Assert.True((await stack.Platform.RestartAppAsync(stack.Target, CancellationToken.None)).Ok);
            Assert.True((await stack.Platform.SetProxyAsync(stack.Target, "10.0.0.1", 9000, CancellationToken.None))
                .Ok);
            Assert.True((await stack.Platform.InstallCaAsync(stack.Target, "ca.cer", CancellationToken.None)).Ok);
            Assert.Equal("up_to_date",
                (await stack.Platform.DriveStoreUpdateAsync(stack.Target, CancellationToken.None)).Action);
        }
    }

    [Fact]
    public void NoFakeTypeExceptTheRunnerItselfTakesATransport() {
        var offenders = typeof(FakeDevicePlatform).Assembly.GetTypes()
            .Where(t => t.Namespace == typeof(FakeDevicePlatform).Namespace)
            .Where(t => t != typeof(RefusingProcessRunner))
            .Where(t => t.GetConstructors().Any(c => c.GetParameters()
                .Any(p => typeof(IProcessRunner).IsAssignableFrom(p.ParameterType))))
            .Select(t => t.Name)
            .ToList();
        Assert.Empty(offenders);
    }

    [Fact]
    public void ImplementsTheTransportInterfaceWithoutUsingOne() =>
        Assert.IsAssignableFrom<IProcessRunner>(new RefusingProcessRunner());
}
