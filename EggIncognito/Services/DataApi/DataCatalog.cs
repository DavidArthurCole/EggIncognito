using System.Text.Json;
using EggIncognito.Core.Services.Assets;
using EggIncognito.GameData;
using Google.Protobuf;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EggIncognito.Services.DataApi;

public sealed class DataCatalog
{
    private static readonly JsonSerializerOptions SnakeJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
    };

    private static readonly JsonSerializerOptions CamelJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly IReadOnlyList<DataSource> _sources;
    private readonly Dictionary<string, DataSource> _byRoute;

    public DataCatalog()
    {
        _sources = Build();
        _byRoute = _sources
            .Where(s => s.WireRoute is not null && s.Provenance == DataProvenance.WireFixture)
            .ToDictionary(s => s.WireRoute!, StringComparer.Ordinal);
    }

    public IReadOnlyList<DataSource> Sources => _sources;

    public DataSource? ById(string group, string id) =>
        _sources.FirstOrDefault(s =>
            string.Equals(s.Group, group, StringComparison.Ordinal) &&
            string.Equals(s.Id, id, StringComparison.Ordinal));

    public DataSource? ByWireRoute(string route) => _byRoute.GetValueOrDefault(route);

    public IReadOnlyList<DataSource> ByGroup(string group) =>
        _sources.Where(s => string.Equals(s.Group, group, StringComparison.Ordinal)).ToArray();

    public IReadOnlyList<DataSource> EgressSources() =>
        _sources.Where(s => s.Refresh.Egress && s.WireRoute is not null).ToArray();

    public IReadOnlyList<string> PeriodicalFeeds() =>
        _sources
            .Where(s => string.Equals(s.Group, "periodical", StringComparison.Ordinal) && s.Feed is not null)
            .Select(s => s.Feed!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    public string UrlFor(DataSource s) => $"/api/v1/data/{s.Group}/{s.Id}";

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

        Derived("colleggtibles", "Colleggtibles", "Custom-egg buff defs extracted from get_periodicals.",
            "ei/get_periodicals", ProduceColleggtibles),
        Derived("eiafx", "eiafx data", "Artifact families/tiers extracted from ei_afx/config + get_config icons.",
            "ei_afx/config", ProduceEiAfx),
        Derived("boost-costs", "Boost costs", "Boost price/token/SE costs extracted from get_config.",
            "ei/get_config", ProduceBoostCosts),

        Embedded("gamedata-boost", "GameData boosts", "boosts.json", "boosts.json"),
        Embedded("gamedata-research", "GameData research", "research.json", "research.json"),
        Embedded("gamedata-hab", "GameData habs", "habs.json", "habs.json"),
        Embedded("gamedata-artifact", "GameData artifacts", "artifacts.json", "artifacts.json"),
        Embedded("gamedata-colleggtibles", "GameData colleggtibles", "colleggtibles.json", "colleggtibles.json"),

        new DataSource("icon", "asset", "Game icon", "Boost/artifact icon PNG by asset name.",
            DataProvenance.Asset, DataAccess.Public, null, null, new DataRefresh(false), true, ProduceIcon),
    ];

    private static DataSource Wire(string id, string display, string desc, string route, string? feed,
        Func<string, byte[]> egressRequest) =>
        new(id, "periodical", display, desc, DataProvenance.WireFixture, DataAccess.Authenticated,
            route, feed, new DataRefresh(true), false,
            (ctx, ct) => Task.FromResult(FixtureJson(ctx, route)), egressRequest);

    private static DataSource Derived(string id, string display, string desc, string route,
        Func<DataProduceContext, CancellationToken, Task<DataPayload?>> produce) =>
        new(id, "derived", display, desc, DataProvenance.DerivedExtract, DataAccess.Public,
            route, null, new DataRefresh(false), false, produce);

    private static DataSource Embedded(string id, string display, string desc, string resource) =>
        new(id, "gamedata", display, desc, DataProvenance.GameDataEmbedded, DataAccess.Public,
            null, null, new DataRefresh(false), false,
            (ctx, ct) => Task.FromResult(EmbeddedJson(resource)));

    private static string DefaultsDir(DataProduceContext ctx)
    {
        var config = ctx.Services.GetRequiredService<IConfiguration>();
        var root = ContentRoot.Resolve(config["ContentRoot"]);
        return Path.Combine(root, "Endpoints", "default");
    }

    private static string FixturePath(DataProduceContext ctx, string route)
    {
        var parts = route.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var file = parts[^1] + ".json";
        return Path.Combine(DefaultsDir(ctx), Path.Combine(parts[..^1]), file);
    }

    private static DataPayload? FixtureJson(DataProduceContext ctx, string route)
    {
        var path = FixturePath(ctx, route);
        return File.Exists(path) ? DataPayload.Json(File.ReadAllText(path)) : null;
    }

    private static string? FixtureText(DataProduceContext ctx, string route)
    {
        var path = FixturePath(ctx, route);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    private static DataPayload? EmbeddedJson(string resource)
    {
        var asm = typeof(ColleggtibleCatalog).Assembly;
        var full = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(resource, StringComparison.Ordinal));
        if (full is null) return null;
        using var stream = asm.GetManifestResourceStream(full)!;
        using var reader = new StreamReader(stream);
        return DataPayload.Json(reader.ReadToEnd());
    }

    private static Task<DataPayload?> ProduceColleggtibles(DataProduceContext ctx, CancellationToken ct)
    {
        var json = FixtureText(ctx, "ei/get_periodicals");
        if (json is null) return Task.FromResult<DataPayload?>(null);
        var extract = ColleggtibleExtractor.FromPeriodicalsJson(json);
        var dto = new
        {
            count = extract.Eggs.Count,
            eggs = extract.Eggs.Select(e => new { e.Identifier, e.Dimension, e.TierValues }),
            contractEggMap = extract.ContractEggMap,
        };
        return Task.FromResult<DataPayload?>(DataPayload.Json(JsonSerializer.Serialize(dto, CamelJson)));
    }

    private static Task<DataPayload?> ProduceBoostCosts(DataProduceContext ctx, CancellationToken ct)
    {
        var json = FixtureText(ctx, "ei/get_config");
        if (json is null) return Task.FromResult<DataPayload?>(null);
        var costs = BoostCostExtractor.FromConfigJson(json);
        var dto = new
        {
            count = costs.Count,
            costs = costs.Select(kv => new { boostId = kv.Key, kv.Value.Price, kv.Value.TokenPrice, kv.Value.SeRequired }),
        };
        return Task.FromResult<DataPayload?>(DataPayload.Json(JsonSerializer.Serialize(dto, CamelJson)));
    }

    private static Task<DataPayload?> ProduceEiAfx(DataProduceContext ctx, CancellationToken ct)
    {
        var afx = FixtureText(ctx, "ei_afx/config");
        if (afx is null) return Task.FromResult<DataPayload?>(null);
        var configJson = FixtureText(ctx, "ei/get_config");
        IReadOnlyDictionary<string, string> icons = new Dictionary<string, string>();
        if (configJson is not null)
        {
            try { icons = DlcArtifactIcons.FromConfigJson(configJson); }
            catch { icons = new Dictionary<string, string>(); }
        }
        var data = EiAfxDataBuilder.BuildFromJson(afx, icons, "captured@ei_afx/config + get_config");
        return Task.FromResult<DataPayload?>(DataPayload.Json(JsonSerializer.Serialize(data, SnakeJson)));
    }

    private static async Task<DataPayload?> ProduceIcon(DataProduceContext ctx, CancellationToken ct)
    {
        var name = ctx.Name;
        if (string.IsNullOrEmpty(name) || name.IndexOfAny(['/', '\\', '.', ' ']) >= 0) return null;
        var assets = ctx.Services.GetRequiredService<GameAssetProvider>();
        var result = await assets.GetAsync(new GameAssetKey("icon", null, name), ct);
        if (!result.Ok || result.Asset is null) return null;
        return new DataPayload(result.Asset.Bytes, result.Asset.ContentType);
    }
}
