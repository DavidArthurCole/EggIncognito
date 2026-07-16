using EggIncognito.Services.Backfill;

namespace EggIncognito.Tests;
public class StoreImportersTests
{
    [Fact]
    public void Play_SoftwareVersion_Parses()
    {
        var html = """<script>{"softwareVersion":"1.35.7","other":1}</script>""";
        Assert.Equal("1.35.7", StoreParse.PlayVersion(html));
    }

    [Fact]
    public void Play_CurrentVersionBlock_Parses()
    {
        var html = "<div>Current Version</div><span class=\"v\">1.36.0</span>";
        Assert.Equal("1.36.0", StoreParse.PlayVersion(html));
    }

    [Fact]
    public void Play_NoVersion_ReturnsNull() => Assert.Null(StoreParse.PlayVersion("<html>nothing</html>"));

    [Fact]
    public void AppStore_Lookup_ParsesFirstResultVersion()
    {
        var json = """{"resultCount":1,"results":[{"version":"1.35.7","trackName":"Egg, Inc."}]}""";
        Assert.Equal("1.35.7", StoreParse.AppStoreVersion(json));
    }

    [Fact]
    public void AppStore_EmptyResults_ReturnsNull() =>
        Assert.Null(StoreParse.AppStoreVersion("""{"resultCount":0,"results":[]}"""));

    [Fact]
    public void AppStore_Malformed_ReturnsNull() => Assert.Null(StoreParse.AppStoreVersion("not json"));
}
