using System.Text.Json;
using EggIncognito.Data.Models;
using EggIncognito.GameData;
using EggIncognito.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;

namespace EggIncognito.Controllers;

[ApiController]
[Route("api/periodicals")]
[EnableRateLimiting("read")]
public sealed class PeriodicalsController(
    ICurrentUser currentUser,
    IConfiguration config,
    IServiceProvider services) : ControllerBase
{
    private static readonly JsonSerializerOptions SnakeJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
    };

    private string Root => ContentRoot.Resolve(config["ContentRoot"]);
    private string DefaultsDir => Path.Combine(Root, "Endpoints", "default");

    private static readonly Dictionary<string, string[]> Feeds = new(StringComparer.Ordinal)
    {
        ["periodicals"] = ["ei", "get_periodicals.json"],
        ["afx-config"] = ["ei_afx", "config.json"],
        ["season-infos"] = ["ei_ctx", "get_season_infos_v2.json"],
    };

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
            colleggtibles = new
            {
                count = col.Eggs.Count,
                provenance = string.IsNullOrEmpty(col.BinaryVersion) ? col.Status : col.BinaryVersion,
                eggs = col.Eggs.Select(e => new { e.Identifier, dimension = DimensionName(e.Dimension), e.TierValues }),
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
            feeds = Feeds.Keys.Select(FeedInfo).ToArray(),
        });
    }

    [HttpGet("feed/{name}")]
    public IActionResult Feed(string name)
    {
        if (RequireAdmin() is { } no) return no;
        if (!Feeds.TryGetValue(name, out var parts)) return NotFound(new { error = "unknown feed" });

        var path = Path.Combine(DefaultsDir, parts[0], parts[1]);
        if (!System.IO.File.Exists(path)) return NotFound(new { error = "no capture on disk" });

        var json = System.IO.File.ReadAllText(path);
        return Content(json, "application/json");
    }

    [HttpGet("eiafx-data")]
    public IActionResult EiAfxData()
    {
        if (RequireAdmin() is { } no) return no;

        var path = Path.Combine(DefaultsDir, "ei_afx", "config.json");
        if (!System.IO.File.Exists(path)) return NotFound(new { error = "no ei_afx/config capture" });

        try
        {
            var json = System.IO.File.ReadAllText(path);
            var icons = LoadArtifactIcons();
            var data = EiAfxDataBuilder.BuildFromJson(json, icons, "captured@ei_afx/config + get_config");
            return Content(JsonSerializer.Serialize(data, SnakeJson), "application/json");
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    private IReadOnlyDictionary<string, string> LoadArtifactIcons()
    {
        var path = Path.Combine(DefaultsDir, "ei", "get_config.json");
        if (!System.IO.File.Exists(path)) return new Dictionary<string, string>();
        try { return DlcArtifactIcons.FromConfigJson(System.IO.File.ReadAllText(path)); }
        catch { return new Dictionary<string, string>(); }
    }

    private static readonly IReadOnlyDictionary<int, string> DimNames =
        ColleggtibleCatalog.DimensionCodes.ToDictionary(kv => kv.Value, kv => kv.Key);

    private static string DimensionName(int code) => DimNames.TryGetValue(code, out var n) ? n : code.ToString();

    private object FeedInfo(string name)
    {
        var parts = Feeds[name];
        var path = Path.Combine(DefaultsDir, parts[0], parts[1]);
        var exists = System.IO.File.Exists(path);
        return new
        {
            name,
            path = $"{parts[0]}/{Path.GetFileNameWithoutExtension(parts[1])}",
            present = exists,
            bytes = exists ? new FileInfo(path).Length : 0,
        };
    }
}
