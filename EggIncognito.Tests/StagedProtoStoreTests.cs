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
    public async Task Reject_HidesAndBlocksReoffer()
    {
        await using var db = new EggIncognitoDbContext(Opts);
        var store = new StagedProtoStore(db, new ProtoRegistryStore(db));
        await store.OfferAsync("android", "1.5", "5", null, "p", "sha3", "proto", null, "u", default);
        var id = (await store.PendingAsync(default))[0].Id;
        Assert.True(await store.RejectAsync(id, "dup", "admin", default));
        Assert.Equal(StagedProtoStore.OfferResult.AlreadyPending, // rejected sha blocks re-offer
            await store.OfferAsync("android", "1.5", "5", null, "p", "sha3", "proto", null, "u", default));
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
        Assert.Equal(1, res.Skipped);  // the missing-build one
        Assert.Equal(0, res.Failed);
    }
}
