namespace EggIncognito.Tests.ProtoStaging;


public class StagedProtoApiTests {
    [Fact]
    public void OfferResult_EnumNames_LowercaseForJson() {
        Assert.Equal("staged", EggIncognito.Data.Services.StagedProtoStore.OfferResult.Staged.ToString().ToLowerInvariant());
        Assert.Equal("alreadyinregistry", EggIncognito.Data.Services.StagedProtoStore.OfferResult.AlreadyInRegistry.ToString().ToLowerInvariant());
    }
}
