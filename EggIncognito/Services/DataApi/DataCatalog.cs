using System.Text.Json;
using System.Text.Json.Nodes;
using EggIncognito.Core.Services.Assets;
using EggIncognito.GameData;
using Ei;
using Google.Protobuf;

namespace EggIncognito.Services.DataApi;

public sealed class DataCatalog {
    private static readonly JsonSerializerOptions SnakeJson = new() {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true
    };

    private static readonly JsonSerializerOptions IndentedJson = new() {
        WriteIndented = true
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

    public IReadOnlyList<string> FeedWireRoutes() =>
        Sources
            .Where(s => s.Feed is not null && s.WireRoute is not null)
            .Select(s => s.WireRoute!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

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

    private static IReadOnlyList<DataSource> Build() => [
        Wire("get_periodicals", "Periodicals", "Raw get_periodicals response fixture.",
            "ei/get_periodicals", "periodicals",
            p => new GetPeriodicalsRequest { Rinfo = new BasicRequestInfo { Platform = p } }.ToByteArray()),
        Wire("afx-config", "Artifacts config", "Raw ei_afx/config response fixture.",
            "ei_afx/config", "afx-config",
            p => new ArtifactsConfigurationRequest { Rinfo = new BasicRequestInfo { Platform = p } }.ToByteArray()),
        Wire("season-infos", "Season infos", "Raw get_season_infos_v2 response fixture.",
            "ei_ctx/get_season_infos_v2", "season-infos",
            p => new BasicRequestInfo { Platform = p }.ToByteArray(), listed: false),
        Wire("config", "Game config", "Raw get_config response fixture.",
            "ei/get_config", null,
            p => new ConfigRequest { Rinfo = new BasicRequestInfo { Platform = p } }.ToByteArray()),

        new("colleggtibles", "periodical", "Colleggtibles",
            "Custom-egg buff defs derived at request time from the freshest captured get_periodicals.",
            DataProvenance.DerivedExtract, DataAccess.Public,
            null, null, new DataRefresh(false), false,
            ProduceColleggtibles,
            Extends: "get_periodicals"),
        new("eiafx", "periodical", "eiafx data",
            "Artifact families/tiers extracted from ei_afx/config + get_config icons.",
            DataProvenance.DerivedExtract, DataAccess.Public,
            "ei_afx/config", null, new DataRefresh(false), false,
            ProduceEiAfx, Extends: "afx-config"),

        Derived("boost-catalog", "Boost catalog",
            "All 33 boosts: identity, costs, effects and durations, extracted from boostmanager + get_config.",
            (_, _) => Task.FromResult(BoostCatalogPayload())),
        Embedded("mission", "Missions", "Home-screen mission goals extracted from missiondata.", "missions.json"),
        Derived("research-common", "Common research",
            "Common research lines extracted from researchdata.",
            (_, _) => Task.FromResult(ResearchPayload(epic: false))),
        Derived("research-epic", "Epic research",
            "Epic research lines extracted from researchdata.",
            (_, _) => Task.FromResult(ResearchPayload(epic: true))),

        new("icon", "asset", "Game icon", "Boost/artifact icon PNG by asset name.",
            DataProvenance.Asset, DataAccess.Public, null, null, new DataRefresh(false), true, ProduceIcon)
    ];

    private static DataSource Wire(string id, string display, string desc, string route, string? feed,
        Func<string, byte[]> egressRequest, bool listed = true) =>
        new(id, "periodical", display, desc, DataProvenance.WireFixture, DataAccess.Authenticated,
            route, feed, new DataRefresh(true), false,
            (ctx, _) => Task.FromResult(FixtureJson(ctx.Services, route)), egressRequest, Listed: listed);

    private static DataSource Embedded(string id, string display, string desc, string resource) =>
        Derived(id, display, desc, (_, _) => Task.FromResult(EmbeddedJson(resource)));

    private static DataSource Derived(string id, string display, string desc,
        Func<DataProduceContext, CancellationToken, Task<DataPayload?>> produce) =>
        new(id, "gamedata", display, desc, DataProvenance.GameDataEmbedded, DataAccess.Public,
            null, null, new DataRefresh(false), false, produce);

    private static string DefaultsDir(IServiceProvider services) {
        var config = services.GetRequiredService<IConfiguration>();
        string root = ContentRoot.Resolve(config["ContentRoot"]);
        return Path.Combine(root, "Endpoints", "default");
    }

    internal static string FixturePath(IServiceProvider services, string route) {
        string[] parts = route.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string file = parts[^1] + ".json";
        return Path.Combine(DefaultsDir(services), Path.Combine(parts[..^1]), file);
    }

    private static DataPayload? FixtureJson(IServiceProvider services, string route) {
        string path = FixturePath(services, route);
        return File.Exists(path) ? DataPayload.Json(File.ReadAllText(path)) : null;
    }

    internal static string? FixtureText(IServiceProvider services, string route) {
        string path = FixturePath(services, route);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    private static DataPayload? EmbeddedJson(string resource) {
        string? text = EmbeddedText(resource);
        return text is null ? null : DataPayload.Json(text);
    }

    private static string? EmbeddedText(string resource) {
        var asm = typeof(ColleggtibleCatalog).Assembly;
        string? full = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(resource, StringComparison.Ordinal));
        if (full is null) return null;
        using var stream = asm.GetManifestResourceStream(full)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static DataPayload? BoostCatalogPayload() {
        string? catalogText = EmbeddedText("boost-catalog.json");
        string? boostsText = EmbeddedText("boosts.json");
        if (catalogText is null && boostsText is null) return null;

        var catalogDoc = catalogText is null ? null : JsonNode.Parse(catalogText)!.AsObject();
        var boostsDoc = boostsText is null ? null : JsonNode.Parse(boostsText)!.AsObject();

        var identity = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        var effects = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        var order = new List<string>();

        if (catalogDoc?["boosts"] is JsonArray catalogBoosts) {
            foreach (var node in catalogBoosts) {
                var row = node!.AsObject();
                string id = row["id"]!.GetValue<string>();
                identity[id] = row;
                order.Add(id);
            }
        }

        if (boostsDoc?["rows"] is JsonArray boostRows) {
            foreach (var node in boostRows) {
                var row = node!.AsObject();
                string id = row["id"]!.GetValue<string>();
                effects[id] = row;
                if (!identity.ContainsKey(id)) order.Add(id);
            }
        }

        var boosts = new JsonArray();
        foreach (string id in order) {
            var merged = new JsonObject { ["id"] = id };
            identity.TryGetValue(id, out var idRow);
            effects.TryGetValue(id, out var fxRow);
            var meta = fxRow?["meta"]?.AsObject();
            CopyField(merged, "displayName", idRow);
            CopyField(merged, "price", idRow, meta);
            CopyField(merged, "tokenPrice", idRow, meta);
            CopyField(merged, "seRequired", idRow, meta);
            CopyField(merged, "iconAsset", idRow, meta);
            CopyField(merged, "target", fxRow);
            CopyField(merged, "combineMode", fxRow);
            CopyField(merged, "magnitude", fxRow);
            CopyField(merged, "kind", meta);
            CopyField(merged, "durationSeconds", meta);
            boosts.Add(merged);
        }

        var provenance = new JsonObject();
        if (boostsDoc?["provenance"] is JsonObject boostsProv) {
            foreach ((string key, var value) in boostsProv) provenance[key] = value?.DeepClone();
        }

        if (catalogDoc?["provenance"] is JsonObject catalogProv) {
            foreach ((string key, var value) in catalogProv) provenance[key] = value?.DeepClone();
        }

        var output = new JsonObject {
            ["boosts"] = boosts,
            ["binaryVersion"] = (catalogDoc?["binaryVersion"] ?? boostsDoc?["binaryVersion"])?.DeepClone(),
            ["provenance"] = provenance
        };
        return DataPayload.Json(output.ToJsonString(IndentedJson));
    }

    private static void CopyField(JsonObject target, string key, params JsonObject?[] sources) {
        foreach (var source in sources) {
            if (source is not null && source.TryGetPropertyValue(key, out var value) && value is not null) {
                target[key] = value.DeepClone();
                return;
            }
        }
    }

    private static DataPayload? ResearchPayload(bool epic) {
        string? text = EmbeddedText("research.json");
        if (text is null) return null;
        var doc = JsonNode.Parse(text)!.AsObject();
        var rows = new JsonArray();
        if (doc["rows"] is JsonArray all) {
            foreach (var node in all) {
                bool isEpic = node?["meta"]?["epic"]?.GetValue<bool>() ?? false;
                if (isEpic == epic) rows.Add(node!.DeepClone());
            }
        }

        var output = new JsonObject {
            ["rows"] = rows,
            ["binaryVersion"] = doc["binaryVersion"]?.DeepClone(),
            ["provenance"] = doc["provenance"]?.DeepClone()
        };
        return DataPayload.Json(output.ToJsonString(IndentedJson));
    }

    private static Task<DataPayload?> ProduceColleggtibles(DataProduceContext ctx, CancellationToken ct) {
        var live = LiveColleggtibleSource.Derive(ctx.Services, "ei/get_periodicals");
        return Task.FromResult(live is null
            ? EmbeddedJson("colleggtibles.json")
            : DataPayload.Json(live.Json));
    }

    private static Task<DataPayload?> ProduceEiAfx(DataProduceContext ctx, CancellationToken ct) {
        string? afx = FixtureText(ctx.Services, "ei_afx/config");
        if (afx is null) return Task.FromResult<DataPayload?>(null);
        string? configJson = FixtureText(ctx.Services, "ei/get_config");
        IReadOnlyDictionary<string, string> icons = new Dictionary<string, string>();
        if (configJson is not null) {
            try {
                icons = DlcArtifactIcons.FromConfigJson(configJson);
            } catch {
                icons = new Dictionary<string, string>();
            }
        }

        var data = EiAfxDataBuilder.BuildFromJson(afx, icons);
        return Task.FromResult<DataPayload?>(DataPayload.Json(JsonSerializer.Serialize(data, SnakeJson)));
    }

    private static async Task<DataPayload?> ProduceIcon(DataProduceContext ctx, CancellationToken ct) {
        string? name = ctx.Name;
        if (string.IsNullOrEmpty(name) || name.IndexOfAny(['/', '\\', '.', ' ']) >= 0) return null;
        var assets = ctx.Services.GetRequiredService<GameAssetProvider>();
        var result = await assets.GetAsync(new GameAssetKey("icon", null, name), ct);
        return !result.Ok || result.Asset is null
            ? null
            : new DataPayload(result.Asset.Bytes, result.Asset.ContentType);
    }
}
