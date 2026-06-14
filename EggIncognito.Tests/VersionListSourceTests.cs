using EggIncognito.Services.Backfill.Sources;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace EggIncognito.Tests;

// Pure parse tests over canned fixtures, no network. Each fixture is a realistic snippet of the source's
// payload; the test asserts the parsed ListedVersion list. Resilience is asserted too: garbage in yields
// an empty list, never an exception.
public class VersionListSourceTests
{
    // Fandom (MediaWiki parse-API wikitext)

    private const string FandomJson = """
    {
      "parse": {
        "title": "Version History",
        "wikitext": {
          "*": "{| class=\"wikitable\"\n|-\n! Version !! Date !! Changes\n|-\n| 1.35.7 || January 5, 2024 || Added [[Artifacts|new artifacts]] and bug fixes\n|-\n| 1.34.1 || December 12, 2023 || '''Holiday event''' tweaks\n|-\n| 1.33.0 || 2023-11-01 || Performance improvements\n|}"
        }
      }
    }
    """;

    [Fact]
    public void Fandom_Parses_Versions_Dates_Changelog()
    {
        var json = FandomJson;
        // ExtractWikitext is private; the test drives ParseWikitext over the unwrapped text directly.
        var wikitext = "{| class=\"wikitable\"\n|-\n! Version !! Date !! Changes\n"
            + "|-\n| 1.35.7 || January 5, 2024 || Added [[Artifacts|new artifacts]] and bug fixes\n"
            + "|-\n| 1.34.1 || December 12, 2023 || '''Holiday event''' tweaks\n"
            + "|-\n| 1.33.0 || 2023-11-01 || Performance improvements\n|}";
        Assert.Contains("1.35.7", json); // fixture sanity

        var list = FandomSource.ParseWikitext(wikitext);
        Assert.Equal(3, list.Count);

        Assert.Equal("1.35.7", list[0].AppVersion);
        Assert.Equal(new DateTimeOffset(2024, 1, 5, 0, 0, 0, TimeSpan.Zero), list[0].ReleaseDate);
        Assert.Equal("Added new artifacts and bug fixes", list[0].Changelog);

        Assert.Equal("1.34.1", list[1].AppVersion);
        Assert.Equal(new DateTimeOffset(2023, 12, 12, 0, 0, 0, TimeSpan.Zero), list[1].ReleaseDate);
        Assert.Equal("Holiday event tweaks", list[1].Changelog);

        Assert.Equal("1.33.0", list[2].AppVersion);
        Assert.Equal(new DateTimeOffset(2023, 11, 1, 0, 0, 0, TimeSpan.Zero), list[2].ReleaseDate);
    }

    [Fact]
    public void Fandom_Garbage_Is_Empty_NoThrow()
    {
        Assert.Empty(FandomSource.ParseWikitext("not a wiki table at all"));
        Assert.Empty(FandomSource.ParseWikitext(""));
    }

    [Fact]
    public void Fandom_Dedups_RepeatedVersion()
    {
        var wt = "|-\n| 1.20.0 || 2023-01-01 ||\n|-\n| 1.20.0 || 2023-01-02 ||";
        var list = FandomSource.ParseWikitext(wt);
        Assert.Single(list);
        Assert.Equal("1.20.0", list[0].AppVersion);
    }

    // Uptodown (HTML)

    private const string UptodownHtml = """
    <div class="content-versions">
      <div class="version-item">
        <div class="version">1.35.7</div>
        <span class="date">Jan 5, 2024</span>
      </div>
      <div class="version-item">
        <div class="version">1.34.1</div>
        <span class="date">Dec 12, 2023</span>
      </div>
    </div>
    """;

    [Fact]
    public void Uptodown_Parses_Version_Date()
    {
        var list = UptodownSource.ParseHtml(UptodownHtml);
        Assert.Equal(2, list.Count);
        Assert.Equal("1.35.7", list[0].AppVersion);
        Assert.Equal(new DateTimeOffset(2024, 1, 5, 0, 0, 0, TimeSpan.Zero), list[0].ReleaseDate);
        Assert.Equal("1.34.1", list[1].AppVersion);
        Assert.Equal(new DateTimeOffset(2023, 12, 12, 0, 0, 0, TimeSpan.Zero), list[1].ReleaseDate);
    }

    [Fact]
    public void Uptodown_Garbage_Is_Empty_NoThrow()
    {
        Assert.Empty(UptodownSource.ParseHtml("<html><body>nothing here</body></html>"));
        Assert.Empty(UptodownSource.ParseHtml(""));
    }

    // APKPure (HTML)

    private const string ApkPureHtml = """
    <ul class="ver-wrap">
      <li><a data-dt-version="1.35.7" href="/down">
        <span class="update-on">Jan 5, 2024</span></a></li>
      <li><a data-dt-version="1.34.1" href="/down">
        <span class="update-on">Dec 12, 2023</span></a></li>
    </ul>
    """;

    [Fact]
    public void ApkPure_Parses_Version_Date()
    {
        var list = ApkPureSource.ParseHtml(ApkPureHtml);
        Assert.Equal(2, list.Count);
        Assert.Equal("1.35.7", list[0].AppVersion);
        Assert.Equal(new DateTimeOffset(2024, 1, 5, 0, 0, 0, TimeSpan.Zero), list[0].ReleaseDate);
        Assert.Equal("1.34.1", list[1].AppVersion);
    }

    [Fact]
    public void ApkPure_Garbage_Is_Empty_NoThrow()
    {
        Assert.Empty(ApkPureSource.ParseHtml("<div>unparseable</div>"));
        Assert.Empty(ApkPureSource.ParseHtml(""));
    }

    // iTunes (lookup JSON)

    private const string ItunesJson = """
    {
      "resultCount": 1,
      "results": [
        {
          "version": "1.35.7",
          "currentVersionReleaseDate": "2024-01-05T08:00:00Z",
          "releaseNotes": "Bug fixes and improvements"
        }
      ]
    }
    """;

    [Fact]
    public void Itunes_Parses_Current_Version()
    {
        var list = ItunesSource.ParseJson(ItunesJson);
        Assert.Single(list);
        Assert.Equal("1.35.7", list[0].AppVersion);
        Assert.Equal(new DateTimeOffset(2024, 1, 5, 8, 0, 0, TimeSpan.Zero), list[0].ReleaseDate);
        Assert.Equal("Bug fixes and improvements", list[0].Changelog);
    }

    [Fact]
    public void Itunes_Empty_Results_Is_Empty()
    {
        Assert.Empty(ItunesSource.ParseJson("""{ "resultCount": 0, "results": [] }"""));
    }

    [Fact]
    public void Itunes_Garbage_Is_Empty_NoThrow()
    {
        Assert.Empty(ItunesSource.ParseJson("not json"));
        Assert.Empty(ItunesSource.ParseJson(""));
    }

    // Internet Archive (advancedsearch JSON)

    private const string ArchiveJson = """
    {
      "response": {
        "numFound": 3,
        "docs": [
          { "identifier": "egg-inc-1.0.2", "title": "Egg, Inc. (1.0.2, iOS 7.0)", "date": "2016-08-01T00:00:00Z" },
          { "identifier": "egginc-1.3.5-ios", "title": "Egg Inc 1.3.5", "date": "2017-02-14T00:00:00Z" },
          { "identifier": "egg-inc-misc", "title": "Egg Inc miscellaneous assets" }
        ]
      }
    }
    """;

    [Fact]
    public void Archive_Parses_Versions_From_Title()
    {
        var list = InternetArchiveSource.ParseJson(ArchiveJson);
        Assert.Equal(2, list.Count);
        Assert.Equal("1.0.2", list[0].AppVersion);
        Assert.Equal(new DateTimeOffset(2016, 8, 1, 0, 0, 0, TimeSpan.Zero), list[0].ReleaseDate);
        Assert.Equal("1.3.5", list[1].AppVersion);
    }

    [Fact]
    public void Archive_Falls_Back_To_Identifier()
    {
        var list = InternetArchiveSource.ParseJson("""
        { "response": { "docs": [ { "identifier": "egg-inc-2.5.1", "title": "Egg Inc no version word" } ] } }
        """);
        Assert.Single(list);
        Assert.Equal("2.5.1", list[0].AppVersion);
    }

    [Fact]
    public void Archive_Dedups_RepeatedVersion()
    {
        var list = InternetArchiveSource.ParseJson("""
        { "response": { "docs": [
          { "identifier": "a", "title": "Egg Inc 1.0.0" },
          { "identifier": "b", "title": "Egg Inc 1.0.0 reupload" } ] } }
        """);
        Assert.Single(list);
    }

    [Fact]
    public void Archive_Garbage_Is_Empty_NoThrow()
    {
        Assert.Empty(InternetArchiveSource.ParseJson("not json"));
        Assert.Empty(InternetArchiveSource.ParseJson(""));
        Assert.Empty(InternetArchiveSource.ParseJson("""{ "response": { "docs": [] } }"""));
        Assert.Empty(InternetArchiveSource.ParseJson("""{ "no": "response" }"""));
    }

    // Unset AppStore:BundleId falls back to the known Egg Inc bundle id and still fetches.

    private sealed class CapturingHandler(string body) : HttpMessageHandler
    {
        public string? RequestedUri { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            RequestedUri = request.RequestUri?.ToString();
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(body),
            });
        }
    }

    private sealed class StubFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    [Fact]
    public async Task Itunes_Unset_BundleId_Uses_Default_And_Fetches()
    {
        var handler = new CapturingHandler(ItunesJson);
        var config = new ConfigurationBuilder().Build(); // AppStore:BundleId unset
        var src = new ItunesSource(new StubFactory(handler), config, NullLogger<ItunesSource>.Instance);

        var list = await src.FetchAsync(CancellationToken.None);

        Assert.Single(list);
        Assert.Equal("1.35.7", list[0].AppVersion);
        Assert.Contains("com.auxbrain.egginc", handler.RequestedUri);
    }

    [Fact]
    public async Task Itunes_Config_BundleId_Overrides_Default()
    {
        var handler = new CapturingHandler(ItunesJson);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["AppStore:BundleId"] = "com.example.app" })
            .Build();
        var src = new ItunesSource(new StubFactory(handler), config, NullLogger<ItunesSource>.Instance);

        await src.FetchAsync(CancellationToken.None);

        Assert.Contains("com.example.app", handler.RequestedUri);
    }
}
