using System.Text;
using EggIncognito.Data.Models;
using EggIncognito.GameData;
using EggIncognito.Services;
using EggIncognito.Services.DataApi;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;

namespace EggIncognito.Controllers;

[ApiController]
[Route("api/periodicals")]
[EggIncognito.Services.Auth.ApiAccess(EggIncognito.Services.Auth.ApiAccessLevel.Admin)]
[EnableRateLimiting("read")]
public sealed class PeriodicalsController(
    ICurrentUser currentUser,
    IConfiguration config,
    DataCatalog catalog,
    IServiceProvider services) : ControllerBase
{
    private string Root => ContentRoot.Resolve(config["ContentRoot"]);
    private string DefaultsDir => Path.Combine(Root, "Endpoints", "default");

    private IEnumerable<DataSource> WireSources => catalog.ByGroup("periodical");

    private IActionResult? RequireAdmin() =>
        currentUser.IsAtLeast(UserRole.Admin) ? null : StatusCode(403, new { error = "admin role required" });

    [HttpGet("summary")]
    public IActionResult Summary()
    {
        if (RequireAdmin() is { } no) return no;

        var extracted = new List<object>();
        object? colleggtibles = null;
        if (services.GetService(typeof(IGameDataProvider)) is IGameDataProvider provider)
        {
            foreach (var f in provider.Families)
                extracted.Add(new
                {
                    key = f.Key,
                    count = f.Effects.Count,
                    provenance = (f as EmbeddedEffectFamily)?.Status ?? "",
                });

            var col = provider.Colleggtibles;
            var icons = LoadColleggtibleIcons();
            colleggtibles = new
            {
                count = col.Eggs.Count,
                provenance = string.IsNullOrEmpty(col.BinaryVersion) ? col.Status : col.BinaryVersion,
                eggs = col.Eggs.Select(e => new
                {
                    e.Identifier,
                    dimension = DimensionName(e.Dimension),
                    e.TierValues,
                    icon = icons.GetValueOrDefault(e.Identifier),
                }),
            };
        }

        var platforms = Array.Empty<object>();
        var configEnabled = false;
        if (services.GetService(typeof(GameConfigStore)) is GameConfigStore store)
        {
            configEnabled = store.Enabled;
            platforms = store.List()
                .Select(c => (object)new { platform = c.Platform, savedAt = c.SavedAt, bytes = c.Bytes })
                .ToArray();
        }

        return Ok(new
        {
            extracted,
            colleggtibles,
            config = new { enabled = configEnabled, platforms },
            feeds = WireSources.Select(FeedInfo).ToArray(),
        });
    }

    [HttpGet("feed/{name}")]
    public async Task<IActionResult> Feed(string name, CancellationToken ct)
    {
        if (RequireAdmin() is { } no) return no;
        var src = WireSources.FirstOrDefault(s => string.Equals(s.Feed, name, StringComparison.Ordinal));
        if (src is null) return NotFound(new { error = "unknown feed" });

        var payload = await src.Produce(new DataProduceContext(HttpContext, null), ct);
        if (payload is null) return NotFound(new { error = "no capture on disk" });
        return Content(Encoding.UTF8.GetString(payload.Bytes), "application/json");
    }

    [HttpGet("gamedata/{key}")]
    public async Task<IActionResult> GameData(string key, CancellationToken ct)
    {
        if (RequireAdmin() is { } no) return no;
        var src = catalog.ById("gamedata", $"gamedata-{key}");
        if (src is null) return NotFound(new { error = "unknown dataset" });

        var payload = await src.Produce(new DataProduceContext(HttpContext, null), ct);
        if (payload is null) return NotFound(new { error = "resource not found" });
        return Content(Encoding.UTF8.GetString(payload.Bytes), "application/json");
    }

    [HttpGet("eiafx-data")]
    public async Task<IActionResult> EiAfxData(CancellationToken ct)
    {
        if (RequireAdmin() is { } no) return no;
        var src = catalog.ById("derived", "eiafx");
        if (src is null) return NotFound(new { error = "eiafx source missing" });

        var payload = await src.Produce(new DataProduceContext(HttpContext, null), ct);
        if (payload is null) return NotFound(new { error = "no ei_afx/config capture" });
        return Content(Encoding.UTF8.GetString(payload.Bytes), "application/json");
    }

    private IReadOnlyDictionary<string, string> LoadColleggtibleIcons()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var route = catalog.ById("derived", "colleggtibles")?.WireRoute;
        if (route is null) return map;
        var path = FixturePath(route);
        if (!System.IO.File.Exists(path)) return map;
        try
        {
            var per = Ei.PeriodicalsResponse.Parser.ParseJson(System.IO.File.ReadAllText(path));
            foreach (var egg in per.Contracts?.CustomEggs ?? Enumerable.Empty<Ei.CustomEgg>())
            {
                var url = egg.Icon?.Url;
                if (!string.IsNullOrEmpty(egg.Identifier) && !string.IsNullOrEmpty(url))
                    map[egg.Identifier] = url;
            }
        }
        catch { }
        return map;
    }

    private string FixturePath(string route)
    {
        var parts = route.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var file = parts[^1] + ".json";
        return Path.Combine(DefaultsDir, Path.Combine(parts[..^1]), file);
    }

    private static readonly IReadOnlyDictionary<int, string> DimNames =
        ColleggtibleCatalog.DimensionCodes.ToDictionary(kv => kv.Value, kv => kv.Key);

    private static string DimensionName(int code) => DimNames.TryGetValue(code, out var n) ? n : code.ToString();

    private object FeedInfo(DataSource src)
    {
        var route = src.WireRoute!;
        var path = FixturePath(route);
        var exists = System.IO.File.Exists(path);
        return new
        {
            name = src.Feed,
            path = route,
            present = exists,
            bytes = exists ? new FileInfo(path).Length : 0,
        };
    }
}
