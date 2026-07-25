using EggIncognito.Services;
using EggIncognito.Services.Assets;
using EggIncognito.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EggIncognito.Controllers;

[ApiController]
[Route("api/ship-assets")]
[ApiAccess(ApiAccessLevel.Public)]
[EnableRateLimiting("read")]
public sealed class ShipAssetsController(IConfiguration config) : ControllerBase {
    [HttpGet("list")]
    public IActionResult List() {
        string? dir = config["ShipAssets:OutputDir"];
        if (string.IsNullOrEmpty(dir)) return Ok(new { ships = Array.Empty<string>() });
        string shipsDir = Path.Combine(dir, "ships");
        if (!Directory.Exists(shipsDir)) return Ok(new { ships = Array.Empty<string>() });
        string?[] ships = [
            .. Directory.EnumerateFiles(shipsDir, "*.glb")
                .Select(Path.GetFileNameWithoutExtension)
                .Where(n => !string.IsNullOrEmpty(n))
                .OrderBy(n => n, StringComparer.Ordinal)
        ];
        return Ok(new { ships });
    }


    [HttpGet("glb/{name}")]
    public IActionResult Glb(string name, [FromQuery] string? animate, [FromQuery] float seconds) {
        if (!ShipNameMap.All.Any(s => string.Equals(s.EnumName, name, StringComparison.Ordinal)))
            return NotFound(new { error = "unknown ship name" });
        string? dir = config["ShipAssets:OutputDir"];
        if (string.IsNullOrEmpty(dir)) return NotFound(new { error = "no ShipAssets:OutputDir configured" });
        string path = Path.Combine(dir, "ships", $"{name}.glb");
        if (!System.IO.File.Exists(path)) return NotFound(new { error = "ship not exported yet" });

        byte[] bytes = System.IO.File.ReadAllBytes(path);
        if (!string.IsNullOrEmpty(animate)) {
            var opts = new GltfAnimator.Options(
                GltfAnimator.ParseKind(animate), seconds > 0 ? seconds : 6f);
            var r = GltfAnimator.Animate(bytes, opts);
            if (r.Ok) bytes = r.Glb!;
        }

        return File(bytes, "model/gltf-binary", $"{name}.glb");
    }
}
