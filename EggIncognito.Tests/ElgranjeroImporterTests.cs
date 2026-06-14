using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.Services.Backfill;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace EggIncognito.Tests;

// The elgranjero importer's decision logic against a stubbed GitHub client + a fake store, DB-free. The
// fake store is registered in a real ServiceCollection so the importer's own scope (it opens one inside
// RunAsync) resolves it exactly as production would resolve the scoped ProtoRegistryStore.
public class ElgranjeroImporterTests
{
    private sealed record Upsert(
        string Platform, string AppVersion, string Build, string? ClientVersion,
        string? ProtoText, bool WriteProto, string Source);

    private sealed class FakeStore : IProtoBackfillStore
    {
        public List<Upsert> Upserts { get; } = [];
        public Dictionary<string, ProtoVersion> Existing { get; } = [];

        public Task<ProtoVersion?> GetAsync(string platform, string build, CancellationToken ct = default) =>
            Task.FromResult(Existing.TryGetValue($"{platform}/{build}", out var r) ? r : null);

        public Task BackfillUpsertAsync(
            string platform, string appVersion, string build, string? clientVersion, string package,
            string? protoText, string? protoSha, string? messageIndex, bool writeProto,
            string apkRef, DateTimeOffset detectedAt, string source, CancellationToken ct = default)
        {
            Upserts.Add(new Upsert(platform, appVersion, build, clientVersion, protoText, writeProto, source));
            return Task.CompletedTask;
        }
    }

    // Canned commit list + per-commit proto, no HTTP.
    private sealed class StubGitHub(
        IEnumerable<GitHubClient.Commit> commits, Func<string, string?> protoForSha) : IGitHubClient
    {
        public async IAsyncEnumerable<GitHubClient.Commit> CommitsAsync(
            string repo, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            foreach (var c in commits) { yield return c; await Task.Yield(); }
        }

        public Task<string?> FileAtAsync(string repo, string sha, string[] paths, CancellationToken ct = default) =>
            Task.FromResult(protoForSha(sha));
    }

    private static IServiceScopeFactory ScopeFactoryWith(IProtoBackfillStore store)
    {
        var services = new ServiceCollection();
        services.AddSingleton(store);
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    private static ElgranjeroImporter Importer(IGitHubClient github, IProtoBackfillStore store) =>
        new(github, ScopeFactoryWith(store), NullLogger<ElgranjeroImporter>.Instance);

    [Fact]
    public async Task ImportsVersionCommits_SkipsNonVersion_WritesProto()
    {
        var commits = new[]
        {
            new GitHubClient.Commit("aaaaaaa0000", "ClientVersion: 72, AppVersion: 1.35.7, Build: 111343", DateTimeOffset.UtcNow),
            new GitHubClient.Commit("bbbbbbb0000", "Updated workflows to add workflow_dispatch", DateTimeOffset.UtcNow),
            new GitHubClient.Commit("ccccccc0000", "ClientVersion: 71, AppVersion: 1.35.6, Build: 111000", DateTimeOffset.UtcNow),
        };
        var store = new FakeStore();
        var github = new StubGitHub(commits, _ => "message Foo { }\nenum Bar { }");

        var n = await Importer(github, store).RunAsync();

        Assert.Equal(2, n);
        Assert.Equal(2, store.Upserts.Count);
        Assert.All(store.Upserts, u => Assert.Equal("elgranjero", u.Source));
        Assert.All(store.Upserts, u => Assert.True(u.WriteProto));
        Assert.Contains(store.Upserts, u => u.Build == "111343" && u.AppVersion == "1.35.7" && u.ClientVersion == "72");
        Assert.Contains(store.Upserts, u => u.Build == "111000");
    }

    [Fact]
    public async Task ExistingFarmRow_NotProtoOverwritten()
    {
        var commits = new[]
        {
            new GitHubClient.Commit("ddddddd0000", "ClientVersion: 72, AppVersion: 1.35.7, Build: 111343", DateTimeOffset.UtcNow),
        };
        var store = new FakeStore();
        store.Existing["android/111343"] = new ProtoVersion { Platform = "android", Build = "111343", Source = "farm" };
        var github = new StubGitHub(commits, _ => "message Foo { }");

        await Importer(github, store).RunAsync();

        var u = Assert.Single(store.Upserts);
        Assert.False(u.WriteProto); // farm proto is authoritative
        Assert.Equal("elgranjero", u.Source); // metadata still upserted (fills nulls)
    }

    [Fact]
    public async Task MissingProto_StillUpsertsMetadata_NoProtoWrite()
    {
        var commits = new[]
        {
            new GitHubClient.Commit("eeeeeee0000", "ClientVersion: 72, AppVersion: 1.35.7, Build: 111343", DateTimeOffset.UtcNow),
        };
        var store = new FakeStore();
        var github = new StubGitHub(commits, _ => null); // no proto at this commit

        await Importer(github, store).RunAsync();

        var u = Assert.Single(store.Upserts);
        Assert.False(u.WriteProto);
        Assert.Null(u.ProtoText);
    }
}
