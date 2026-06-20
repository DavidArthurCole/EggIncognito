using Xunit;

namespace EggIncognito.Tests.ProtoStaging;

// No-DB behavior of the staged endpoints (the only part runnable without Postgres). Full flows are covered
// by the skipped StagedProtoStoreTests against a live DB. Guards that the OfferResult enum names lowercase
// to the JSON values the controller emits.
public class StagedProtoApiTests
{
    [Fact]
    public void OfferResult_EnumNames_LowercaseForJson()
    {
        Assert.Equal("staged", EggIncognito.Data.Services.StagedProtoStore.OfferResult.Staged.ToString().ToLowerInvariant());
        Assert.Equal("alreadyinregistry", EggIncognito.Data.Services.StagedProtoStore.OfferResult.AlreadyInRegistry.ToString().ToLowerInvariant());
    }
}
