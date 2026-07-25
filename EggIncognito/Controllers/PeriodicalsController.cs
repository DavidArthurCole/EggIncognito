using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using EggIncognito.GameData;
using EggIncognito.Services;
using EggIncognito.Services.Auth;
using EggIncognito.Services.DataApi;
using Ei;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using EggIdentity.Contract;

namespace EggIncognito.Controllers;

[ApiController]
[Route("api/periodicals")]
[ApiAccess(ApiAccessLevel.Admin)]
[EnableRateLimiting("read")]
public sealed class PeriodicalsController(
    ICurrentUser currentUser,
    IConfiguration config,
    DataCatalog catalog,
    IServiceProvider services) : ControllerBase {
    private static readonly JsonSerializerOptions ProvenanceJson = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly Dictionary<int, string> DimNames =
        ColleggtibleCatalog.DimensionCodes.ToDictionary(kv => kv.Value, kv => kv.Key);

    private string Root => ContentRoot.Resolve(config["ContentRoot"]);
    private string DefaultsDir => Path.Combine(Root, "Endpoints", "default");

    private IEnumerable<DataSource> WireSources =>
        catalog.ByGroup("periodical").Where(s => s.Provenance == DataProvenance.WireFixture);

    private ObjectResult? RequireAdmin() =>
        currentUser.IsAtLeast(UserRole.Admin) ? null : StatusCode(403, new { error = "admin role required" });

    [HttpGet("summary")]
    public IActionResult Summary() {
        if (RequireAdmin() is { } no) return no;

        var extracted = new List<object>();
        object? colleggtibles = null;
        if (services.GetService(typeof(IGameDataProvider)) is IGameDataProvider provider) {
            foreach (var f in provider.Families) {
                extracted.Add(new {
                    key = f.Key,
                    count = f.Effects.Count,
                    provenance = JsonSerializer.Serialize(f.Provenance, ProvenanceJson)
                });
            }

            var col = provider.Colleggtibles;
            var icons = LoadColleggtibleIcons();
            colleggtibles = new {
                count = col.Eggs.Count,
                gameVersion = col.GameVersion,
                provenance = JsonSerializer.Serialize(col.Provenance, ProvenanceJson),
                eggs = col.Eggs.Select(e => new {
                    e.Identifier,
                    dimension = DimensionName(e.Dimension),
                    e.TierValues,
                    icon = icons.GetValueOrDefault(e.Identifier)
                })
            };
        }

        object[] platforms = [];
        bool configEnabled = false;
        if (services.GetService(typeof(GameConfigStore)) is GameConfigStore store) {
            configEnabled = store.Enabled;
            platforms = [
                .. store.List().Select(c => (object)new { platform = c.Platform, savedAt = c.SavedAt, bytes = c.Bytes })
            ];
        }

        return Ok(new {
            extracted,
            colleggtibles,
            config = new { enabled = configEnabled, platforms },
            feeds = WireSources.Select(FeedInfo).ToArray()
        });
    }

    [HttpGet("feed/{name}")]
    public async Task<IActionResult> Feed(string name, CancellationToken ct) {
        if (RequireAdmin() is { } no) return no;
        var src = WireSources.FirstOrDefault(s => string.Equals(s.Feed, name, StringComparison.Ordinal));
        if (src is null) return NotFound(new { error = "unknown feed" });

        var payload = await src.Produce(new DataProduceContext(HttpContext, null), ct);
        return payload is null
            ? NotFound(new { error = "no capture on disk" })
            : Content(Encoding.UTF8.GetString(payload.Bytes), "application/json");
    }

    [HttpGet("gamedata/{key}")]
    public async Task<IActionResult> GameData(string key, CancellationToken ct) {
        if (RequireAdmin() is { } no) return no;
        var src = catalog.ById("gamedata", key);
        if (src is null) return NotFound(new { error = "unknown dataset" });

        var payload = await src.Produce(new DataProduceContext(HttpContext, null), ct);
        return payload is null
            ? NotFound(new { error = "resource not found" })
            : Content(Encoding.UTF8.GetString(payload.Bytes), "application/json");
    }

    [HttpGet("eiafx-data")]
    public async Task<IActionResult> EiAfxData(CancellationToken ct) {
        if (RequireAdmin() is { } no) return no;
        var src = catalog.ByChild("periodical", "afx-config", "eiafx");
        if (src is null) return NotFound(new { error = "eiafx source missing" });

        var payload = await src.Produce(new DataProduceContext(HttpContext, null), ct);
        return payload is null
            ? NotFound(new { error = "no ei_afx/config capture" })
            : Content(Encoding.UTF8.GetString(payload.Bytes), "application/json");
    }

    private Dictionary<string, string> LoadColleggtibleIcons() {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        string? route = catalog.ById("periodical", "get_periodicals")?.WireRoute;
        if (route is null) return map;
        string path = FixturePath(route);
        if (!System.IO.File.Exists(path)) return map;
        try {
            var per = PeriodicalsResponse.Parser.ParseJson(System.IO.File.ReadAllText(path));
            foreach (var egg in per.Contracts?.CustomEggs ?? []) {
                string? url = egg.Icon?.Url;
                if (!string.IsNullOrEmpty(egg.Identifier) && !string.IsNullOrEmpty(url))
                    map[egg.Identifier] = url;
            }
        } catch {
        }

        return map;
    }

    private string FixturePath(string route) {
        string[] parts = route.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string file = parts[^1] + ".json";
        return Path.Combine(DefaultsDir, Path.Combine(parts[..^1]), file);
    }

    private static string DimensionName(int code) =>
        DimNames.TryGetValue(code, out string? n) ? n : code.ToString(CultureInfo.InvariantCulture);

    private object FeedInfo(DataSource src) {
        string route = src.WireRoute!;
        string path = FixturePath(route);
        bool exists = System.IO.File.Exists(path);
        return new {
            name = src.Feed,
            path = route,
            present = exists,
            bytes = exists ? new FileInfo(path).Length : 0
        };
    }
}
