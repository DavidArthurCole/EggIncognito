using System.Text.Json;
using EggIncognito.Core.Services.Assets;
using EggIncognito.GameData;
using Google.Protobuf;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EggIncognito.Services.DataApi;

public sealed class DataCatalog {
    private static readonly JsonSerializerOptions SnakeJson = new() {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
    };
    private readonly Dictionary<string, DataSource> _byRoute;

    public DataCatalog() {
        Sources = Build();
        _byRoute = Sources
            .Where(s => s.WireRoute is not null && s.Provenance == DataProvenance.WireFixture)
            .ToDictionary(s => s.WireRoute!, StringComparer.Ordinal);
    }

    public IReadOnlyList<DataSource> Sources { get; }

    public DataSource? ById(string group, string id) =>
        Sources.FirstOrDefault(s =>
            string.Equals(s.Group, group, StringComparison.Ordinal) &&
            string.Equals(s.Id, id, StringComparison.Ordinal));

    public DataSource? ByChild(string group, string parentId, string subId) =>
        Sources.FirstOrDefault(s =>
            string.Equals(s.Group, group, StringComparison.Ordinal) &&
            string.Equals(s.Extends, parentId, StringComparison.Ordinal) &&
            string.Equals(s.Id, subId, StringComparison.Ordinal));

    public IReadOnlyList<DataSource> Children(DataSource parent) =>
        Sources.Where(s => string.Equals(s.Extends, parent.Id, StringComparison.Ordinal)).ToArray();

    public DataSource? ByWireRoute(string route) => _byRoute.GetValueOrDefault(route);

    public IReadOnlyList<DataSource> ByGroup(string group) =>
        Sources.Where(s => string.Equals(s.Group, group, StringComparison.Ordinal)).ToArray();

    public IReadOnlyList<DataSource> EgressSources() =>
        Sources.Where(s => s.Refresh.Egress && s.WireRoute is not null).ToArray();

    public IReadOnlyList<string> PeriodicalFeeds() =>
        Sources
            .Where(s => string.Equals(s.Group, "periodical", StringComparison.Ordinal) && s.Feed is not null)
            .Select(s => s.Feed!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    public string UrlFor(DataSource s) => s.Extends is null
        ? $"/api/v1/data/{s.Group}/{s.Id}"
        : $"/api/v1/data/{s.Group}/{s.Extends}/{s.Id}";

    public static string EgressUrl(DataSource s) => $"https://www.auxbrain.com/{s.WireRoute}";

    private static IReadOnlyList<DataSource> Build() =>
    [
        Wire("get_periodicals", "Periodicals", "Raw get_periodicals response fixture.",
            "ei/get_periodicals", "periodicals",
            p => new Ei.GetPeriodicalsRequest { Rinfo = new Ei.BasicRequestInfo { Platform = p } }.ToByteArray()),
        Wire("afx-config", "Artifacts config", "Raw ei_afx/config response fixture.",
            "ei_afx/config", "afx-config",
            p => new Ei.ArtifactsConfigurationRequest { Rinfo = new Ei.BasicRequestInfo { Platform = p } }.ToByteArray()),
        Wire("season-infos", "Season infos", "Raw get_season_infos_v2 response fixture.",
            "ei_ctx/get_season_infos_v2", "season-infos",
            p => new Ei.BasicRequestInfo { Platform = p }.ToByteArray()),
        Wire("config", "Game config", "Raw get_config response fixture.",
            "ei/get_config", null,
            p => new Ei.ConfigRequest { Rinfo = new Ei.BasicRequestInfo { Platform = p } }.ToByteArray()),

        new DataSource("colleggtibles", "periodical", "Colleggtibles",
            "Custom-egg buff defs extracted from get_periodicals.",
            DataProvenance.GameDataEmbedded, DataAccess.Public,
            null, null, new DataRefresh(false), false,
            (_, _) => Task.FromResult(EmbeddedJson("colleggtibles.json")),
            Extends: "get_periodicals"),
        new DataSource("eiafx", "periodical", "eiafx data",
            "Artifact families/tiers extracted from ei_afx/config + get_config icons.",
            DataProvenance.DerivedExtract, DataAccess.Public,
            "ei_afx/config", null, new DataRefresh(false), false,
            ProduceEiAfx, Extends: "afx-config"),

        Embedded("boost", "Boosts", "boosts.json", "boosts.json"),
        Embedded("boost-catalog", "Boost catalog", "All 33 boosts: identity + costs, extracted from boostmanager + get_config.", "boost-catalog.json"),
        Embedded("egg-catalog", "Egg catalog", "Egg names and base values extracted from eggdata.", "eggs.json"),
        Embedded("dimension", "Boost dimensions", "Boost dimension ids extracted from boostmanager.", "dimensions.json"),
        Embedded("mission", "Missions", "Home-screen mission goals extracted from missiondata.", "missions.json"),
        Embedded("vehicle", "Vehicles", "Vehicle names and shipping capacities extracted from vehicledata.", "vehicles.json"),
        Embedded("research", "Research", "research.json", "research.json"),
        Embedded("hab", "Habs", "habs.json", "habs.json"),
        Embedded("artifact", "Artifacts", "artifacts.json", "artifacts.json"),

        new DataSource("icon", "asset", "Game icon", "Boost/artifact icon PNG by asset name.",
            DataProvenance.Asset, DataAccess.Public, null, null, new DataRefresh(false), true, ProduceIcon),
    ];

    private static DataSource Wire(string id, string display, string desc, string route, string? feed,
        Func<string, byte[]> egressRequest) =>
        new(id, "periodical", display, desc, DataProvenance.WireFixture, DataAccess.Authenticated,
            route, feed, new DataRefresh(true), false,
            (ctx, _) => Task.FromResult(FixtureJson(ctx, route)), egressRequest);

    private static DataSource Embedded(string id, string display, string desc, string resource) =>
        new(id, "gamedata", display, desc, DataProvenance.GameDataEmbedded, DataAccess.Public,
            null, null, new DataRefresh(false), false,
            (_, _) => Task.FromResult(EmbeddedJson(resource)));

    private static string DefaultsDir(DataProduceContext ctx) {
        var config = ctx.Services.GetRequiredService<IConfiguration>();
        var root = ContentRoot.Resolve(config["ContentRoot"]);
        return Path.Combine(root, "Endpoints", "default");
    }

    private static string FixturePath(DataProduceContext ctx, string route) {
        var parts = route.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var file = parts[^1] + ".json";
        return Path.Combine(DefaultsDir(ctx), Path.Combine(parts[..^1]), file);
    }

    private static DataPayload? FixtureJson(DataProduceContext ctx, string route) {
        var path = FixturePath(ctx, route);
        return File.Exists(path) ? DataPayload.Json(File.ReadAllText(path)) : null;
    }

    private static string? FixtureText(DataProduceContext ctx, string route) {
        var path = FixturePath(ctx, route);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    private static DataPayload? EmbeddedJson(string resource) {
        var asm = typeof(ColleggtibleCatalog).Assembly;
        var full = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(resource, StringComparison.Ordinal));
        if (full is null) return null;
        using var stream = asm.GetManifestResourceStream(full)!;
        using var reader = new StreamReader(stream);
        return DataPayload.Json(reader.ReadToEnd());
    }

    private static Task<DataPayload?> ProduceEiAfx(DataProduceContext ctx, CancellationToken ct) {
        var afx = FixtureText(ctx, "ei_afx/config");
        if (afx is null) return Task.FromResult<DataPayload?>(null);
        var configJson = FixtureText(ctx, "ei/get_config");
        IReadOnlyDictionary<string, string> icons = new Dictionary<string, string>();
        if (configJson is not null) {
            try { icons = DlcArtifactIcons.FromConfigJson(configJson); } catch { icons = new Dictionary<string, string>(); }
        }
        var data = EiAfxDataBuilder.BuildFromJson(afx, icons);
        return Task.FromResult<DataPayload?>(DataPayload.Json(JsonSerializer.Serialize(data, SnakeJson)));
    }

    private static async Task<DataPayload?> ProduceIcon(DataProduceContext ctx, CancellationToken ct) {
        var name = ctx.Name;
        if (string.IsNullOrEmpty(name) || name.IndexOfAny(['/', '\\', '.', ' ']) >= 0) return null;
        var assets = ctx.Services.GetRequiredService<GameAssetProvider>();
        var result = await assets.GetAsync(new GameAssetKey("icon", null, name), ct);
        return !result.Ok || result.Asset is null ? null : new DataPayload(result.Asset.Bytes, result.Asset.ContentType);
    }
}
