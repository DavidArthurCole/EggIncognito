using EggIncognito.Data.Services;

namespace EggIncognito.Tests.ProtoStaging;

public class StagedProtoApiTests {
    [Fact]
    public void OfferResult_EnumNames_LowercaseForJson() {
        Assert.Equal("staged", StagedProtoStore.OfferResult.Staged.ToString().ToLowerInvariant());
        Assert.Equal("alreadyinregistry", StagedProtoStore.OfferResult.AlreadyInRegistry.ToString().ToLowerInvariant());
    }
}
