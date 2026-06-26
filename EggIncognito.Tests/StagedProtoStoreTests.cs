using EggIncognito.Data.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EggIncognito.Tests;

// DB-backed: the repo carries no EF test provider (tests-DB-free rule). These document intended behavior +
// run manually against a live Postgres (eggincognito_test). Skipped in CI.
public class StagedProtoStoreTests
{
    static DbContextOptions<EggIncognitoDbContext> Opts =>
        new DbContextOptionsBuilder<EggIncognitoDbContext>()
            .UseNpgsql("Host=frame;Port=5432;Database=eggincognito_test;Username=ei;Password=ei").Options;

    [Fact(Skip = "requires Postgres; no EF test provider per tests-DB-free repo rule")]
    public async Task Offer_DedupsAgainstPendingAndRegistry()
    {
        await using var db = new EggIncognitoDbContext(Opts);
        var store = new StagedProtoStore(db, new ProtoRegistryStore(db));
        var r1 = await store.OfferAsync("android", "1.40", "111", null, "p", "sha1", "proto", null, "u", default);
        Assert.Equal(StagedProtoStore.OfferResult.Staged, r1);
        var r2 = await store.OfferAsync("android", "1.40", "111", null, "p", "sha1", "proto", null, "u", default);
        Assert.Equal(StagedProtoStore.OfferResult.AlreadyPending, r2);
    }

    [Fact(Skip = "requires Postgres; no EF test provider per tests-DB-free repo rule")]
    public async Task Approve_PromotesToRegistry_ThenRejectsMissingBuild()
    {
        await using var db = new EggIncognitoDbContext(Opts);
        var store = new StagedProtoStore(db, new ProtoRegistryStore(db));
        await store.OfferAsync("android", null, null, null, "p", "sha2", "proto", null, "u", default);
        var pend = await store.PendingAsync(default);
        var id = pend[0].Id;
        // no build/appVersion on the row + none supplied -> MissingBuild
        Assert.Equal(StagedProtoStore.ApproveResult.MissingBuild,
            await store.ApproveAsync(id, null, null, null, null, "admin", default));
        // supply them -> Ok
        Assert.Equal(StagedProtoStore.ApproveResult.Ok,
            await store.ApproveAsync(id, "android", "1.41", "112", "73", "admin", default));
    }

    [Fact(Skip = "requires Postgres; no EF test provider per tests-DB-free repo rule")]
    public async Task Reject_SameData_StaysRejected_RicherData_Revives()
    {
        await using var db = new EggIncognitoDbContext(Opts);
        var store = new StagedProtoStore(db, new ProtoRegistryStore(db));
        // stage with only appVersion (score 1), then reject = "not enough data yet"
        await store.OfferAsync("android", "1.5", null, null, "p", "sha3", "proto", null, "u", default);
        var id = (await store.PendingAsync(default))[0].Id;
        Assert.True(await store.RejectAsync(id, "missing data", "admin", default));
        Assert.Empty(await store.PendingAsync(default));

        // same-or-poorer data re-offer -> stays rejected (reads as AlreadyPending to the offerer), queue empty
        Assert.Equal(StagedProtoStore.OfferResult.AlreadyPending,
            await store.OfferAsync("android", "1.5", null, null, "p", "sha3", "proto", null, "u", default));
        Assert.Empty(await store.PendingAsync(default));

        // RICHER data re-offer (now has build + clientVersion) -> revives back to pending, fields filled
        Assert.Equal(StagedProtoStore.OfferResult.Staged, // Revived maps to Staged for the offerer
            await store.OfferAsync("android", "1.5", "5", "72", "p", "sha3", "proto", null, "u", default));
        var pend = await store.PendingAsync(default);
        Assert.Single(pend);
        Assert.Equal("5", pend[0].Build); // filled on revive
        Assert.Equal("72", pend[0].ClientVersion);
    }

    [Fact(Skip = "requires Postgres; no EF test provider per tests-DB-free repo rule")]
    public async Task Approve_ExistingBuild_MergesInsteadOfBlocking()
    {
        await using var db = new EggIncognitoDbContext(Opts);
        var registry = new ProtoRegistryStore(db);
        var store = new StagedProtoStore(db, registry);
        // existing registry row with NO clientVersion
        await registry.UpsertAsync("android", "1.40", "111", clientVersion: null, package: "p",
            protoSha: "x", apkRef: "", DateTimeOffset.UtcNow, "u", "proto", "farm", ct: default);
        // a staged row for the SAME build that carries the clientVersion
        await store.OfferAsync("android", "1.40", "111", "72", "p", "sha9", "proto2", null, "u", default);
        var id = (await store.PendingAsync(default))[0].Id;
        // approve -> Merged (not blocked), clientVersion filled into the existing row, queue cleared
        Assert.Equal(StagedProtoStore.ApproveResult.Merged,
            await store.ApproveAsync(id, null, null, null, null, "admin", default));
        Assert.Empty(await store.PendingAsync(default));
        var row = await db.ProtoVersions.FirstAsync(p => p.Platform == "android" && p.Build == "111");
        Assert.Equal("72", row.ClientVersion); // filled from the staged row
    }

    [Fact(Skip = "requires Postgres; no EF test provider per tests-DB-free repo rule")]
    public async Task BulkReject_RejectsAllPendingIds()
    {
        await using var db = new EggIncognitoDbContext(Opts);
        var store = new StagedProtoStore(db, new ProtoRegistryStore(db));
        await store.OfferAsync("android", null, null, null, "p", "b1", "proto", null, "u", default);
        await store.OfferAsync("android", null, null, null, "p", "b2", "proto", null, "u", default);
        var ids = (await store.PendingAsync(default)).Select(r => r.Id).ToList();
        var n = await store.BulkRejectAsync(ids, "batch", "admin", default);
        Assert.Equal(2, n);
        Assert.Empty(await store.PendingAsync(default));
    }

    [Fact(Skip = "requires Postgres; no EF test provider per tests-DB-free repo rule")]
    public async Task BulkApprove_PromotesValid_SkipsMissingBuild()
    {
        await using var db = new EggIncognitoDbContext(Opts);
        var store = new StagedProtoStore(db, new ProtoRegistryStore(db));
        await store.OfferAsync("android", "1.40", "111", "72", "p", "ok1", "proto", null, "u", default); // approvable
        await store.OfferAsync("android", null, null, null, "p", "nobuild", "proto", null, "u", default); // missing build
        var pend = await store.PendingAsync(default);
        var items = pend.Select(r => new StagedProtoStore.ApproveItem(
            r.Id, r.Platform, r.AppVersion, r.Build, r.ClientVersion)).ToList();
        var res = await store.BulkApproveAsync(items, "admin", default);
        Assert.Equal(1, res.Approved); // the one with build
        Assert.Equal(1, res.Skipped); // the missing-build one
        Assert.Equal(0, res.Failed);
    }
}
