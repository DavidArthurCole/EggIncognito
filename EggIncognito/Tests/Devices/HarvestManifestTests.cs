using EggIncognito.Core.Services.Devices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace EggIncognito.Tests.Devices;

public class HarvestManifestTests {
    private sealed class DeadRunner : IProcessRunner {
        public Task<ProcessResult> RunAsync(string exe, string[] args, CancellationToken ct) =>
            Task.FromResult(new ProcessResult(1, "", "no device"));
    }

    private static AndroidPlatform Android() =>
        new(new DeadRunner(), new ConfigurationBuilder().Build(), [], [], [],
            NullLogger<AndroidPlatform>.Instance);

    private static IosPlatform Ios() {
        var runner = new DeadRunner();
        var config = new DeviceCaptureConfig();
        return new IosPlatform(new DeviceConnectionFactory(runner, config), config, runner, [], [], [],
            NullLogger<IosPlatform>.Instance);
    }

    [Fact]
    public void BothPlatforms_DeclareTheSameEntryNames() {
        var android = Android().Manifest().Select(e => e.Name).Order(StringComparer.Ordinal).ToList();
        var ios = Ios().Manifest().Select(e => e.Name).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(android, ios);
    }

    [Fact]
    public void EveryEntry_HasAKindAndNoDuplicates() {
        foreach (var manifest in (IReadOnlyList<HarvestEntry>[])[Android().Manifest(), Ios().Manifest()]) {
            Assert.NotEmpty(manifest);
            Assert.All(manifest, e => Assert.False(string.IsNullOrWhiteSpace(e.Kind)));
            Assert.Equal(manifest.Count, manifest.Select(e => e.Name).Distinct(StringComparer.Ordinal).Count());
        }
    }

    [Fact]
    public void UnsupportedEntries_SayWhy() {
        foreach (var manifest in (IReadOnlyList<HarvestEntry>[])[Android().Manifest(), Ios().Manifest()]) {
            foreach (var entry in manifest.Where(e => !e.Supported))
                Assert.False(string.IsNullOrWhiteSpace(entry.UnsupportedNote));
        }
    }

    [Fact]
    public void BothPlatforms_HarvestTheAppBinaryAndMeshes() {
        foreach (var manifest in (IReadOnlyList<HarvestEntry>[])[Android().Manifest(), Ios().Manifest()]) {
            Assert.Contains(manifest, e => e.Name == HarvestEntries.AppBinary && e.Supported);
            Assert.Contains(manifest, e => e.Name == HarvestEntries.Meshes && e.Supported);
        }
    }

    [Fact]
    public async Task UnsupportedEntry_IsNeverAttempted() {
        var android = Android();
        var entry = android.Manifest().First(e => !e.Supported);
        var target = new DeviceTarget("x", "android", "127.0.0.1:5555", "com.auxbrain.egginc");

        var fp = await android.FingerprintAsync(target, entry, default);
        var batch = await android.HarvestAsync(target, entry, new Dictionary<string, string>(), default);

        Assert.Equal(DeviceOutcome.Unsupported, fp.Outcome);
        Assert.Equal(DeviceOutcome.Unsupported, batch.Outcome);
    }
}
