using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using EggIncognito.Services;
using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Controllers;

// Serves the exported ship .glb set (ShipAssets:OutputDir/ships) for the 3D playground. The 7 bundled ships
// are produced by the device export path; this just lists + serves them, optionally animated. (The old
// "resolve the 4 CDN-only orbital ships from a DLCCatalog" path was removed: those ships have no published
// mesh, only 2D icons, so it was a dead end. Shells now flow through ShellsController instead.)
[ApiController]
[Route("api/ship-assets")]
[EnableRateLimiting("read")]
public sealed class ShipAssetsController(IConfiguration config) : ControllerBase
{
    // Lists the exported ship .glb files in ShipAssets:OutputDir/ships, for the playground source picker.
    [HttpGet("list")]
    public IActionResult List()
    {
        var dir = config["ShipAssets:OutputDir"];
        if (string.IsNullOrEmpty(dir)) return Ok(new { ships = Array.Empty<string>() });
        var shipsDir = Path.Combine(dir, "ships");
        if (!Directory.Exists(shipsDir)) return Ok(new { ships = Array.Empty<string>() });
        var ships = Directory.EnumerateFiles(shipsDir, "*.glb")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => !string.IsNullOrEmpty(n))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();
        return Ok(new { ships });
    }

    // Serves one exported ship .glb by enum name, optionally animated (?animate=spin). The enum-name allowlist
    // blocks path traversal. Read-only, public.
    [HttpGet("glb/{name}")]
    public IActionResult Glb(string name, [FromQuery] string? animate, [FromQuery] float seconds)
    {
        if (!ShipNameMap.All.Any(s => string.Equals(s.EnumName, name, StringComparison.Ordinal)))
            return NotFound(new { error = "unknown ship name" });
        var dir = config["ShipAssets:OutputDir"];
        if (string.IsNullOrEmpty(dir)) return NotFound(new { error = "no ShipAssets:OutputDir configured" });
        var path = Path.Combine(dir, "ships", $"{name}.glb");
        if (!System.IO.File.Exists(path)) return NotFound(new { error = "ship not exported yet" });

        var bytes = System.IO.File.ReadAllBytes(path);
        if (!string.IsNullOrEmpty(animate))
        {
            var opts = new Services.Assets.GltfAnimator.Options(
                Services.Assets.GltfAnimator.ParseKind(animate), seconds > 0 ? seconds : 6f);
            var r = Services.Assets.GltfAnimator.Animate(bytes, opts);
            if (r.Ok) bytes = r.Glb!;
        }
        return File(bytes, "model/gltf-binary", $"{name}.glb");
    }
}
