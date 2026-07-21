using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Net.Http.Headers;
using EggIncognito.Core.Services.Assets;

namespace EggIncognito.Controllers;

[ApiController]
[Route("api/assets")]
[EggIncognito.Services.Auth.ApiAccess(EggIncognito.Services.Auth.ApiAccessLevel.Public)]
[EnableRateLimiting("read")]
public sealed class AssetsController(GameAssetProvider assets) : ControllerBase
{
    [HttpGet("icon")]
    public async Task<IActionResult> Icon([FromQuery] string? name, [FromQuery] string? platform, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(name) || name.IndexOfAny(['/', '\\', '.', ' ']) >= 0)
            return BadRequest(new { error = "invalid icon name" });

        var plat = string.IsNullOrEmpty(platform) ? null : platform;
        var result = await assets.GetAsync(new GameAssetKey("icon", plat, name), ct);
        if (!result.Ok || result.Asset is null)
            return NotFound(new { error = result.Diagnostics ?? "icon not available", name });

        Response.GetTypedHeaders().CacheControl = new CacheControlHeaderValue
        {
            Public = true,
            MaxAge = TimeSpan.FromDays(30)
        };
        return File(result.Asset.Bytes, result.Asset.ContentType);
    }
}
