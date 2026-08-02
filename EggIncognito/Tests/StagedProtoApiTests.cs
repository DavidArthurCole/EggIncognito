using EggIncognito.Data.Services;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Tests.ProtoStaging;

public class StagedProtoApiTests {
    private static DbContextOptions<EggIncognitoDbContext> Opts =>
        new DbContextOptionsBuilder<EggIncognitoDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=eggincognito_test;Username=x;Password=x;Timeout=1").Options;

    [Fact]
    public void OfferResult_EnumNames_LowercaseForJson() {
        Assert.Equal("staged", StagedProtoStore.OfferResult.Staged.ToString().ToLowerInvariant());
        Assert.Equal("alreadyinregistry", StagedProtoStore.OfferResult.AlreadyInRegistry.ToString().ToLowerInvariant());
    }

    [Fact(Skip = "needs a reachable Postgres")]
    public async Task OfferAsync_persists_source() {
        await using var db = new EggIncognitoDbContext(Opts);
        var store = new StagedProtoStore(db, new ProtoRegistryStore(db));
        var r = await store.OfferAsync("android", "1.0", "b1", null, null,
            "sha-batch-1", "message X {}", null, "user-1", "batch", CancellationToken.None);
        Assert.Equal(StagedProtoStore.OfferResult.Staged, r);
        var row = await db.StagedProtos.SingleAsync(s => s.ProtoSha == "sha-batch-1");
        Assert.Equal("batch", row.Source);
    }
}
