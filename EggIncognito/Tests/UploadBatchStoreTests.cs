using EggIncognito.Data.Services;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Tests;

public class UploadBatchStoreTests {
    private static DbContextOptions<EggIncognitoDbContext> Opts =>
        new DbContextOptionsBuilder<EggIncognitoDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=eggincognito_test;Username=x;Password=x;Timeout=1").Options;

    [Fact(Skip = "needs a reachable Postgres")]
    public async Task CreateAsync_then_ClaimNext_marks_processing() {
        await using var db = new EggIncognitoDbContext(Opts);
        var store = new UploadBatchStore(db);
        int id = await store.CreateAsync("u1", new[] {
            new UploadBatchStore.NewBatchFile("game.apk", 3, [1, 2, 3])
        }, CancellationToken.None);

        var item = await store.ClaimNextAsync(CancellationToken.None);
        Assert.NotNull(item);
        Assert.Equal("processing", item!.Status);
        Assert.Equal("android", item.Platform);
        var batch = await db.UploadBatches.SingleAsync(b => b.Id == id);
        Assert.Equal("processing", batch.Status);
    }

    [Fact(Skip = "needs a reachable Postgres")]
    public async Task CompleteItem_finishes_batch_and_nulls_bytes() {
        await using var db = new EggIncognitoDbContext(Opts);
        var store = new UploadBatchStore(db);
        int id = await store.CreateAsync("u1", new[] {
            new UploadBatchStore.NewBatchFile("a.ipa", 1, [9])
        }, CancellationToken.None);
        var item = await store.ClaimNextAsync(CancellationToken.None);
        await store.CompleteItemAsync(item!.Id,
            new UploadBatchStore.ItemOutcome("staged", "sha1", "1.0", "b", "77", null),
            CancellationToken.None);

        var reloaded = await db.UploadBatchItems.SingleAsync(i => i.Id == item.Id);
        Assert.Equal("staged", reloaded.Status);
        Assert.Null(reloaded.Bytes);
        Assert.Equal("ios", reloaded.Platform);
        var batch = await db.UploadBatches.SingleAsync(b => b.Id == id);
        Assert.Equal("done", batch.Status);
        Assert.Equal(1, batch.ProcessedItems);
    }

    [Fact]
    public void InferPlatform_maps_extension() {
        Assert.Equal("ios", UploadBatchStore.InferPlatform("X.ipa"));
        Assert.Equal("android", UploadBatchStore.InferPlatform("X.apkm"));
    }
}
