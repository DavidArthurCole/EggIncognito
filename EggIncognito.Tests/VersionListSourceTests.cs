using EggIncognito.Services.Backfill.Sources;

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
}
