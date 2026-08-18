using EggIncognito.Services.DataApi;
using EggIncognito.Services.Feed;
using Ei;
using Google.Protobuf;

namespace EggIncognito.Tests;

public class ConfigAspectsTests {
    private static string Json(IMessage msg) => JsonFormatter.Default.Format(msg);

    private static PeriodicalsResponse Periodicals(params string[] contractIds) {
        var response = new PeriodicalsResponse { Contracts = new ContractsResponse() };
        foreach (string id in contractIds) response.Contracts.Contracts.Add(new Contract { Identifier = id });
        return response;
    }

    private static ConfigResponse Config(params string[] shellSetIds) {
        var response = new ConfigResponse { DlcCatalog = new DLCCatalog() };
        foreach (string id in shellSetIds) response.DlcCatalog.ShellSets.Add(new ShellSetSpec { Identifier = id });
        return response;
    }

    [Fact]
    public void NoPrevious_IsUncharacterised() =>
        Assert.Null(ConfigAspects.Diff(ConfigFeeds.Periodicals, null, Json(Periodicals("a"))));

    [Fact]
    public void Periodicals_ReportsAddedAndRemovedContracts() {
        var change = ConfigAspects.Diff(ConfigFeeds.Periodicals,
            Json(Periodicals("keep", "drop")), Json(Periodicals("keep", "fresh")));

        Assert.NotNull(change);
        Assert.Contains("contracts", change.Changed);
        string[] added = ["contract:fresh"];
        string[] removed = ["contract:drop"];
        Assert.Equal(added, change.Added);
        Assert.Equal(removed, change.Removed);
    }

    [Fact]
    public void Periodicals_CountdownOnly_IsNotAChange() {
        var before = Periodicals();
        before.Events = new EggIncCurrentEvents();
        before.Events.Events.Add(new EggIncEvent { Identifier = "boost", SecondsRemaining = 900 });
        var after = Periodicals();
        after.Events = new EggIncCurrentEvents();
        after.Events.Events.Add(new EggIncEvent { Identifier = "boost", SecondsRemaining = 60 });

        var change = ConfigAspects.Diff(ConfigFeeds.Periodicals, Json(before), Json(after));

        Assert.NotNull(change);
        Assert.False(change.Any);
    }

    [Fact]
    public void Config_ReportsShellSetChanges() {
        var change = ConfigAspects.Diff(ConfigFeeds.Config, Json(Config("classic")), Json(Config("classic", "glacier")));

        Assert.NotNull(change);
        Assert.Contains("shellSets", change.Changed);
        string[] added = ["shellSet:glacier"];
        Assert.Equal(added, change.Added);
        Assert.Empty(change.Removed);
    }

    [Fact]
    public void Config_ShellSetCountdownOnly_IsNotAChange() {
        var before = Config();
        before.DlcCatalog.ShellSets.Add(new ShellSetSpec {
            Identifier = "classic",
            SecondsUntilAvailable = 3600,
            Popularity = 12
        });
        before.DlcCatalog.ShellsShowcaseLastFeaturedTime = 1000;
        var after = Config();
        after.DlcCatalog.ShellSets.Add(new ShellSetSpec {
            Identifier = "classic",
            SecondsUntilAvailable = 30,
            Popularity = 99
        });
        after.DlcCatalog.ShellsShowcaseLastFeaturedTime = 2000;

        var change = ConfigAspects.Diff(ConfigFeeds.Config, Json(before), Json(after));

        Assert.NotNull(change);
        Assert.False(change.Any);
    }

    [Fact]
    public void Seasons_ReportsNewSeason() {
        var before = new ContractSeasonInfos();
        before.Infos.Add(new ContractSeasonInfo { Id = "summer-2026" });
        var after = new ContractSeasonInfos();
        after.Infos.Add(new ContractSeasonInfo { Id = "summer-2026" });
        after.Infos.Add(new ContractSeasonInfo { Id = "fall-2026" });

        var change = ConfigAspects.Diff(ConfigFeeds.Seasons, Json(before), Json(after));

        Assert.NotNull(change);
        Assert.Contains("seasons", change.Changed);
        string[] added = ["season:fall-2026"];
        Assert.Equal(added, change.Added);
    }

    [Fact]
    public void Afx_ReportsArtifactSpecChanges() {
        var before = new ArtifactsConfigurationResponse();
        before.ArtifactParameters.Add(new ArtifactsConfigurationResponse.Types.ArtifactParameters {
            Spec = new ArtifactSpec { Name = ArtifactSpec.Types.Name.OrnateGusset }
        });
        var after = new ArtifactsConfigurationResponse();
        after.ArtifactParameters.Add(new ArtifactsConfigurationResponse.Types.ArtifactParameters {
            Spec = new ArtifactSpec { Name = ArtifactSpec.Types.Name.OrnateGusset }
        });
        after.ArtifactParameters.Add(new ArtifactsConfigurationResponse.Types.ArtifactParameters {
            Spec = new ArtifactSpec { Name = ArtifactSpec.Types.Name.TungstenAnkh }
        });

        var change = ConfigAspects.Diff(ConfigFeeds.Afx, Json(before), Json(after));

        Assert.NotNull(change);
        Assert.Contains("artifacts", change.Changed);
        Assert.Single(change.Added);
    }

    [Fact]
    public void UnparseableJson_IsUncharacterised() =>
        Assert.Null(ConfigAspects.Diff(ConfigFeeds.Config, "{", "{"));
}
