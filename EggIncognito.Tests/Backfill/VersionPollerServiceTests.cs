using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.Services.Backfill;
using EggIncognito.Services.Backfill.Sources;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace EggIncognito.Tests.Backfill;

public class VersionPollerServiceTests
{
    [Fact]
    public async Task PollOnce_UpsertsAll_QueuesExtractForNewOnly()
    {
        var jobs = new FakeJobStore();
        jobs.Known.Add(new KnownVersion { Platform = "android", AppVersion = "1.0", Source = "apkpure" });

        var sp = BuildProvider(jobs,
            android: [new ListedVersion("1.0", null, null), new ListedVersion("1.1", null, null)],
            ios: [new ListedVersion("1.5", null, null)]);

        var poller = new VersionPollerService(
            sp.GetRequiredService<IServiceScopeFactory>(),
            new VersionPollerOptions { Enabled = true, AutoQueueExtract = true, Platforms = ["android", "ios"] },
            TimeProvider.System, NullLogger<VersionPollerService>.Instance);

        await poller.PollOnceAsync(CancellationToken.None);

        Assert.Contains(("android", "1.0"), jobs.Upserts);
        Assert.Contains(("android", "1.1"), jobs.Upserts);
        Assert.Contains(("ios", "1.5"), jobs.Upserts);
        Assert.Contains(("android", "1.1"), jobs.Extracts);
        Assert.Contains(("ios", "1.5"), jobs.Extracts);
        Assert.DoesNotContain(("android", "1.0"), jobs.Extracts);
    }

    [Fact]
    public async Task PollOnce_RespectsPlatformFilter()
    {
        var jobs = new FakeJobStore();
        var sp = BuildProvider(jobs,
            android: [new ListedVersion("1.1", null, null)],
            ios: [new ListedVersion("1.5", null, null)]);

        var poller = new VersionPollerService(
            sp.GetRequiredService<IServiceScopeFactory>(),
            new VersionPollerOptions { Platforms = ["ios"] },
            TimeProvider.System, NullLogger<VersionPollerService>.Instance);

        await poller.PollOnceAsync(CancellationToken.None);

        Assert.Contains(("ios", "1.5"), jobs.Upserts);
        Assert.DoesNotContain(("android", "1.1"), jobs.Upserts);
    }

    [Fact]
    public async Task PollOnce_NoQueue_WhenAutoQueueDisabled()
    {
        var jobs = new FakeJobStore();
        var sp = BuildProvider(jobs, android: [new ListedVersion("2.0", null, null)], ios: []);

        var poller = new VersionPollerService(
            sp.GetRequiredService<IServiceScopeFactory>(),
            new VersionPollerOptions { AutoQueueExtract = false, Platforms = ["android"] },
            TimeProvider.System, NullLogger<VersionPollerService>.Instance);

        await poller.PollOnceAsync(CancellationToken.None);

        Assert.Contains(("android", "2.0"), jobs.Upserts);
        Assert.Empty(jobs.Extracts);
    }

    private static ServiceProvider BuildProvider(
        FakeJobStore jobs, IReadOnlyList<ListedVersion> android, IReadOnlyList<ListedVersion> ios)
    {
        var services = new ServiceCollection();
        services.AddScoped<IBackfillJobStore>(_ => jobs);
        services.AddKeyedScoped<IVersionListSource>("fandom", (_, _) => new FakeSource("fandom", "android", android));
        services.AddKeyedScoped<IVersionListSource>("itunes", (_, _) => new FakeSource("itunes", "ios", ios));
        return services.BuildServiceProvider();
    }

    private sealed class FakeSource(string name, string platform, IReadOnlyList<ListedVersion> versions) : IVersionListSource
    {
        public string Name => name;
        public string Platform => platform;
        public Task<IReadOnlyList<ListedVersion>> FetchAsync(CancellationToken ct) => Task.FromResult(versions);
    }

    private sealed class FakeJobStore : IBackfillJobStore
    {
        public List<KnownVersion> Known { get; } = [];
        public List<(string, string)> Upserts { get; } = [];
        public List<(string, string)> Extracts { get; } = [];

        public Task UpsertKnownAsync(string platform, string appVersion, DateTimeOffset? releaseDate,
            string? changelog, string source, CancellationToken ct = default)
        {
            Upserts.Add((platform, appVersion));
            return Task.CompletedTask;
        }

        public Task StartExtractAsync(string platform, string appVersion, CancellationToken ct = default)
        {
            Extracts.Add((platform, appVersion));
            return Task.CompletedTask;
        }

        public Task<List<KnownVersion>> KnownAsync(CancellationToken ct = default) => Task.FromResult(Known);

        public Task<BackfillJob> StartAsync(string source, string? startedBy, CancellationToken ct = default) => throw new NotSupportedException();
        public Task BumpAsync(int jobId, int imported, string? note = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task FinishAsync(int jobId, string status, string? note = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task<List<BackfillJob>> LatestPerSourceAsync(CancellationToken ct = default) => Task.FromResult(new List<BackfillJob>());
        public Task FinishExtractAsync(string platform, string appVersion, string status, string? note, CancellationToken ct = default) => Task.CompletedTask;
        public Task<List<ExtractJob>> ListExtractJobsAsync(CancellationToken ct = default) => Task.FromResult(new List<ExtractJob>());
    }
}
