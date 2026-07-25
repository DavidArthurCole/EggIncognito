using EggIncognito.GameData;
using EggIncognito.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EggIncognito.Controllers;

[ApiController]
[Route("api/gamedata")]
[ApiAccess(ApiAccessLevel.Public)]
[EnableRateLimiting("read")]
public sealed class GameDataController(IGameDataProvider provider) : ControllerBase {
    [HttpGet("effects")]
    public IActionResult Effects([FromQuery] string? family, [FromQuery] string? target) {
        var effects = family is null
            ? provider.Families.SelectMany(f => f.Effects)
            : provider.All(family);

        if (target is not null && Enum.TryParse<EffectTarget>(target, true, out var t))
            effects = effects.Where(e => e.Target == t);

        return Ok(new {
            families = provider.Families.Select(f => f.Key),
            effects = effects.Select(Project)
        });
    }

    [HttpGet("families")]
    public IActionResult FamilyList() =>
        Ok(provider.Families.Select(f => new { f.Key, count = f.Effects.Count }));

    private static object Project(Effect e) => new {
        e.Family,
        e.Id,
        target = e.Target.ToString(),
        combineMode = e.CombineMode.ToString(),
        e.Magnitude,
        e.MaxLevel,
        e.Meta
    };
}
