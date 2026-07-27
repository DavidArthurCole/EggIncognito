using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using EggIdentity.Contract;
using EggIncognito.Core.Services.Assets;
using EggIncognito.Data.Services;
using EggIncognito.GameData;
using EggIncognito.Services;
using EggIncognito.Services.Auth;
using EggIncognito.Services.DataApi;
using Ei;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

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

            var icons = LoadColleggtibleIcons();
            string? route = catalog.ById("periodical", "get_periodicals")?.WireRoute;
            var live = route is null ? null : LiveColleggtibleSource.Derive(services, route);
            if (live is not null) {
                colleggtibles = new {
                    count = live.Extract.Eggs.Count,
                    gameVersion = live.GameVersion,
                    provenance = JsonSerializer.Serialize(live.Provenance, ProvenanceJson),
                    eggs = live.Extract.Eggs.Select(e => new {
                        e.Identifier,
                        dimension = DimensionName(e.Dimension),
                        e.TierValues,
                        icon = icons.GetValueOrDefault(e.Identifier)
                    })
                };
            } else {
                var col = provider.Colleggtibles;
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

    [HttpGet("seasons")]
    public IActionResult Seasons() {
        if (RequireAdmin() is { } no) return no;
        string? route = catalog.ById("periodical", "season-infos")?.WireRoute;
        if (route is null) return NotFound(new { error = "season source missing" });
        string path = FixturePath(route);
        if (!System.IO.File.Exists(path)) return NotFound(new { error = "no season capture on disk" });

        ContractSeasonInfos infos;
        try {
            infos = ContractSeasonInfos.Parser.ParseJson(System.IO.File.ReadAllText(path));
        } catch (Exception ex) {
            return StatusCode(500, new { error = $"season fixture unreadable: {ex.Message}" });
        }

        return Ok(new { seasons = SeasonList(infos) });
    }

    private static object[] SeasonList(ContractSeasonInfos infos) {
        var list = infos.Infos.ToList();
        double[] starts = ResolveStarts(list);
        return [
            .. list.Select((s, i) => (object)new {
                id = s.Id,
                name = string.IsNullOrEmpty(s.Name) ? PrettySeasonId(s.Id) : s.Name,
                startTime = starts[i],
                startDerived = !(s.HasStartTime && s.StartTime > 0),
                gradeGoals = s.GradeGoals.Select(g => new {
                    grade = g.Grade.ToString(),
                    goals = g.Goals.Select(x => new {
                        cxp = x.Cxp,
                        rewardType = x.RewardType.ToString(),
                        rewardSubType = x.RewardSubType,
                        rewardAmount = x.RewardAmount
                    }).ToArray()
                }).ToArray()
            })
        ];
    }

    private static double[] ResolveStarts(List<ContractSeasonInfo> list) {
        double[] starts = [.. list.Select(s => s.HasStartTime && s.StartTime > 0 ? s.StartTime : 0)];
        int[] known = [.. Enumerable.Range(0, starts.Length).Where(i => starts[i] > 0)];
        if (known.Length == 0) return starts;

        double quarter = 7889400;
        if (known.Length >= 2) {
            double sum = 0;
            int n = 0;
            for (int k = 0; k + 1 < known.Length; k++) {
                int a = known[k], b = known[k + 1];
                if (starts[a] > starts[b] && b > a) {
                    sum += (starts[a] - starts[b]) / (b - a);
                    n++;
                }
            }

            if (n > 0) quarter = sum / n;
        }

        for (int i = 0; i < starts.Length; i++) {
            if (starts[i] > 0) continue;
            int nearest = known.MinBy(j => Math.Abs(j - i));
            starts[i] = starts[nearest] - (i - nearest) * quarter;
        }

        return starts;
    }

    private static string PrettySeasonId(string id) =>
        string.Join(' ', id.Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Length > 0 ? char.ToUpperInvariant(w[0]) + w[1..] : w));

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

    [HttpGet("current")]
    public async Task<IActionResult> Current(CancellationToken ct) {
        if (RequireAdmin() is { } no) return no;
        string? route = catalog.ById("periodical", "get_periodicals")?.WireRoute;
        (string? json, DateTimeOffset? capturedAt) = await ResolveCurrentJson(route, ct);
        if (json is null) return NotFound(new { error = "no periodicals capture available" });

        PeriodicalsResponse per;
        try {
            per = PeriodicalsResponse.Parser.ParseJson(json);
        } catch (Exception ex) {
            return StatusCode(500, new { error = $"periodicals capture unreadable: {ex.Message}" });
        }

        double? serverTime = per.Contracts is { HasServerTime: true } c ? c.ServerTime : null;
        var events = new List<object>();
        var iconCache = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var e in per.Events?.Events ?? []) {
            double? endTime = e.StartTime > 0 && e.Duration > 0
                ? e.StartTime + e.Duration
                : serverTime is { } st ? st + e.SecondsRemaining : null;
            events.Add(new {
                identifier = e.Identifier,
                type = e.Type,
                subtitle = e.Subtitle,
                multiplier = e.Multiplier,
                startTime = e.StartTime,
                duration = e.Duration,
                endTime,
                icon = await ResolveEventIcon(e.Type, iconCache, ct)
            });
        }

        return Ok(new { capturedAt, serverTime, events });
    }

    private async Task<(string? Json, DateTimeOffset? CapturedAt)> ResolveCurrentJson(string? route, CancellationToken ct) {
        if (services.GetService(typeof(EggIncognitoDbContext)) is EggIncognitoDbContext db) {
            try {
                var snap = await db.PeriodicalsSnapshots
                    .OrderByDescending(s => s.CapturedAt)
                    .FirstOrDefaultAsync(ct);
                if (snap is not null) return (snap.ResponseJson, snap.CapturedAt);
                if (route is not null) {
                    var stored = await db.StoredEndpoints
                        .FirstOrDefaultAsync(s => s.Path == route && s.Eid == null, ct);
                    if (stored is not null) return (stored.ResponseJson, null);
                }
            } catch {
            }
        }

        if (route is null) return (null, null);
        string path = FixturePath(route);
        return System.IO.File.Exists(path)
            ? (await System.IO.File.ReadAllTextAsync(path, ct), null)
            : (null, null);
    }

    private async Task<string?> ResolveEventIcon(string type, Dictionary<string, string?> cache, CancellationToken ct) {
        if (string.IsNullOrEmpty(type)) return null;
        if (cache.TryGetValue(type, out string? cached)) return cached;
        string? icon = null;
        if (services.GetService(typeof(GameAssetProvider)) is GameAssetProvider assets) {
            string stem = type.Replace('-', '_');
            string[] candidates = [stem, $"event_{stem}"];
            foreach (string candidate in candidates) {
                var result = await assets.GetAsync(new GameAssetKey("icon", null, candidate), ct);
                if (!result.Ok || result.Asset is null) continue;
                icon = $"/api/v1/data/asset/icon?name={Uri.EscapeDataString(candidate)}";
                break;
            }
        }

        cache[type] = icon;
        return icon;
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
