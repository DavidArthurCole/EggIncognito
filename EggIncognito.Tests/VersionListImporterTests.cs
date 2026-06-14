using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.Services.Backfill;
using EggIncognito.Services.Backfill.Sources;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace EggIncognito.Tests;

// VersionListImporter lifecycle over a fake source + fake job store, DB-free. A good source upserts each
// version and drives the job running -> done with the right imported count; a throwing source marks the
// job failed with the error note. ElgranjeroImporter is covered separately for gating; here the focus is
// the job-tracking + known-versions upsert path the list importer owns.
public class VersionListImporterTests
{
    private sealed class FakeSource(string name, string platform, IReadOnlyList<ListedVersion> versions)
        : IVersionListSource
    {
        public string Name => name;
        public string Platform => platform;
        public Task<IReadOnlyList<ListedVersion>> FetchAsync(CancellationToken ct) => Task.FromResult(versions);
    }

    private sealed class ThrowingSource : IVersionListSource
    {
        public string Name => "boom";
        public string Platform => "android";
        public Task<IReadOnlyList<ListedVersion>> FetchAsync(CancellationToken ct) =>
            throw new InvalidOperationException("scrape failed");
    }

    private sealed class FakeJobStore : IBackfillJobStore
    {
        private int _nextId = 1;
        public List<BackfillJob> Jobs { get; } = [];
        public List<KnownVersion> Known { get; } = [];

        public Task<BackfillJob> StartAsync(string source, string? startedBy, CancellationToken ct = default)
        {
            var job = new BackfillJob { Id = _nextId++, Source = source, Status = "running", StartedBy = startedBy };
            Jobs.Add(job);
            return Task.FromResult(job);
        }

        public Task BumpAsync(int jobId, int imported, string? note = null, CancellationToken ct = default)
        {
            var j = Jobs.First(x => x.Id == jobId);
            j.Imported = imported;
            if (note is not null) j.Note = note;
            return Task.CompletedTask;
        }

        public Task FinishAsync(int jobId, string status, string? note = null, CancellationToken ct = default)
        {
            var j = Jobs.First(x => x.Id == jobId);
            j.Status = status;
            j.FinishedAt = DateTimeOffset.UtcNow;
            if (note is not null) j.Note = note;
            return Task.CompletedTask;
        }

        public Task<List<BackfillJob>> LatestPerSourceAsync(CancellationToken ct = default) =>
            Task.FromResult(Jobs);

        public Task UpsertKnownAsync(string platform, string appVersion, DateTimeOffset? releaseDate,
            string? changelog, string source, CancellationToken ct = default)
        {
            var existing = Known.FirstOrDefault(
                k => k.Platform == platform && k.AppVersion == appVersion && k.Source == source);
            if (existing is null)
                Known.Add(new KnownVersion
                {
                    Platform = platform, AppVersion = appVersion, ReleaseDate = releaseDate,
                    Changelog = changelog, Source = source,
                });
            return Task.CompletedTask;
        }

        public Task<List<KnownVersion>> KnownAsync(CancellationToken ct = default) => Task.FromResult(Known);
    }

    // The importer opens its own DI scope; give it a provider that resolves the fake job store.
    private static ServiceProvider Provider(IBackfillJobStore store)
    {
        var sc = new ServiceCollection();
        sc.AddScoped(_ => store);
        return sc.BuildServiceProvider();
    }

    [Fact]
    public async Task GoodSource_Upserts_Known_And_Marks_Done()
    {
        var jobs = new FakeJobStore();
        var sp = Provider(jobs);
        var importer = new VersionListImporter(
            sp.GetRequiredService<IServiceScopeFactory>(), NullLogger<VersionListImporter>.Instance);

        var source = new FakeSource("fandom", "android",
        [
            new ListedVersion("1.35.7", new DateTimeOffset(2024, 1, 5, 0, 0, 0, TimeSpan.Zero), "notes"),
            new ListedVersion("1.34.1", null, null),
        ]);

        var n = await importer.RunAsync(source, "admin1");

        Assert.Equal(2, n);
        Assert.Equal(2, jobs.Known.Count);
        Assert.Equal("android", jobs.Known[0].Platform);
        var job = Assert.Single(jobs.Jobs);
        Assert.Equal("done", job.Status);
        Assert.Equal(2, job.Imported);
        Assert.Equal("admin1", job.StartedBy);
        Assert.NotNull(job.FinishedAt);
    }

    [Fact]
    public async Task ThrowingSource_Marks_Job_Failed_With_Note()
    {
        var jobs = new FakeJobStore();
        var sp = Provider(jobs);
        var importer = new VersionListImporter(
            sp.GetRequiredService<IServiceScopeFactory>(), NullLogger<VersionListImporter>.Instance);

        var n = await importer.RunAsync(new ThrowingSource(), "admin1");

        Assert.Equal(0, n);
        var job = Assert.Single(jobs.Jobs);
        Assert.Equal("failed", job.Status);
        Assert.Contains("scrape failed", job.Note);
        Assert.Empty(jobs.Known);
    }

    [Fact]
    public async Task BlankVersion_Is_Skipped()
    {
        var jobs = new FakeJobStore();
        var sp = Provider(jobs);
        var importer = new VersionListImporter(
            sp.GetRequiredService<IServiceScopeFactory>(), NullLogger<VersionListImporter>.Instance);

        var source = new FakeSource("uptodown", "android",
        [
            new ListedVersion("", null, null),
            new ListedVersion("1.0.0", null, null),
        ]);

        var n = await importer.RunAsync(source);
        Assert.Equal(1, n);
        Assert.Single(jobs.Known);
    }
}
