using EggIncognito.Services;

namespace EggIncognito.Tests;

public class PageMetaTests {
    [Theory]
    [InlineData("/protos/ios/1.37.0.1", "ei.proto - iOS 1.37.0.1")]
    [InlineData("/protos/android/111358", "ei.proto - Android 111358")]
    [InlineData("/protos/IOS/1.37.0.1", "ei.proto - iOS 1.37.0.1")]
    public void AVersionPathGetsItsOwnTitle(string path, string title) {
        Assert.Equal(title, PageMeta.For(path).Title);
    }

    [Fact]
    public void AVersionDescriptionNamesThePlatformAndBuild() {
        string description = PageMeta.For("/protos/ios/1.37.0.1").Description;
        Assert.Contains("iOS", description, StringComparison.Ordinal);
        Assert.Contains("1.37.0.1", description, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/protos")]
    [InlineData("/protodata")]
    [InlineData("/protos/subscribe")]
    [InlineData("/protos/sources")]
    public void RegistryPathsKeepTheRegistryCard(string path) {
        Assert.Equal("EggIncognito - Proto Registry", PageMeta.For(path).Title);
    }

    [Theory]
    [InlineData("/protos/ios/<script>")]
    [InlineData("/protos/ios/a b")]
    [InlineData("/protos/i0s/1.37.0.1")]
    [InlineData("/protos/ios/")]
    [InlineData("/protos/ios/1.37.0.1/extra")]
    public void JunkFallsBackToThePrefixCardInsteadOfReflecting(string path) {
        Assert.Equal("EggIncognito - Proto Registry", PageMeta.For(path).Title);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/")]
    [InlineData("/nope")]
    public void UnknownPathsGetTheDefault(string? path) {
        Assert.Equal(PageMeta.Default.Title, PageMeta.For(path).Title);
    }
}
