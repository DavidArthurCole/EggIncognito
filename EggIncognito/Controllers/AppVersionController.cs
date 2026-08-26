using EggIncognito.Services;
using EggIncognito.Services.Auth;
using Microsoft.AspNetCore.Mvc;

namespace EggIncognito.Controllers;

[ApiController]
[Route("api/app/version")]
[ApiAccess(ApiAccessLevel.Public)]
public sealed class AppVersionController : ControllerBase {
    private const string RepoUrl = "https://github.com/EggIncTools/EggIncognito";

    private static readonly object Payload = Build();

    private static object Build() {
        var b = BuildInfo.FromAssembly(RepoUrl);
        return new { version = b.Version, sha = b.ShortSha };
    }

    [HttpGet]
    public IActionResult Get() => Ok(Payload);
}
