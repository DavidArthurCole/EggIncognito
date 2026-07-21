using EggIncognito.Services.DataApi;
using EggIncognito.Services.Feed;
using EggIncognito.Services.RateLimiting;
using Xunit;

namespace EggIncognito.Tests;

public class DataApiRegistryTests
{
    [Fact]
    public void Catalog_IdsUniquePerGroup_AndProducersPresent()
    {
        var c = new DataCatalog();
        Assert.NotEmpty(c.Sources);
        foreach (var g in c.Sources.GroupBy(s => s.Group))
            Assert.Equal(g.Count(), g.Select(s => s.Id).Distinct().Count());
        Assert.All(c.Sources, s =>
        {
            Assert.False(string.IsNullOrWhiteSpace(s.Id));
            Assert.False(string.IsNullOrWhiteSpace(s.DisplayName));
            Assert.NotNull(s.Produce);
            Assert.StartsWith("/api/v1/data/", c.UrlFor(s));
        });
    }

    [Fact]
    public void Catalog_ByWireRoute_ResolvesFeed()
    {
        var c = new DataCatalog();
        Assert.Equal("periodicals", c.ByWireRoute("ei/get_periodicals")?.Feed);
        Assert.Equal("afx-config", c.ByWireRoute("ei_afx/config")?.Feed);
        Assert.Equal("season-infos", c.ByWireRoute("ei_ctx/get_season_infos_v2")?.Feed);
    }

    [Fact]
    public void Catalog_EgressSources_HaveRouteAndRequestBuilder()
    {
        var c = new DataCatalog();
        Assert.NotEmpty(c.EgressSources());
        Assert.All(c.EgressSources(), s =>
        {
            Assert.False(string.IsNullOrWhiteSpace(s.WireRoute));
            Assert.NotNull(s.BuildEgressRequest);
        });
    }

    [Fact]
    public void Catalog_PeriodicalFeeds_MatchFeedEventKinds()
    {
        var c = new DataCatalog();
        var feeds = c.PeriodicalFeeds().OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var triggers = FeedEventKinds.Periodicals.Triggers
            .Select(t => t.Value).Where(v => v != "any").OrderBy(x => x, StringComparer.Ordinal).ToArray();
        Assert.Equal(triggers, feeds);
    }

    [Fact]
    public void RateLimitOptions_HasDataPolicies()
    {
        var o = RateLimitOptions.Defaults();
        Assert.True(o.Tiers.ContainsKey("Keyed"));
        Assert.True(o.Policies.ContainsKey("Data"));
        Assert.Equal(1, o.Policies["DataAnon"].PermitLimit);
        Assert.Equal(3600, o.Policies["DataAnon"].WindowSeconds);
    }

    [Fact]
    public void ApiKeyGen_MintDeterministicHash()
    {
        var (full, hash, prefix) = ApiKeyGen.Mint();
        Assert.StartsWith("egi_live_", full);
        Assert.Equal(12, prefix.Length);
        Assert.Equal(hash, ApiKeyGen.HashOf(full));
        Assert.Equal(64, hash.Length);
        var (second, _, _) = ApiKeyGen.Mint();
        Assert.NotEqual(full, second);
    }
}
