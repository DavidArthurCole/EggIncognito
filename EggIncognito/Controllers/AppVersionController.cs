using Microsoft.AspNetCore.Mvc;

namespace EggIncognito.Controllers;

// The build identity of the running server, for the client reconnect watcher: when the version
// differs from the one the page loaded with, the page reloads.
[ApiController]
[Route("api/app/version")]
public sealed class AppVersionController : ControllerBase
{
    private const string RepoUrl = "https://github.com/davidarthurcole/EggIncognito";

    private static readonly object Payload = Build();

    private static object Build()
    {
        var b = EggIncognito.Services.BuildInfo.FromAssembly(RepoUrl);
        return new { version = b.Version, sha = b.ShortSha };
    }

    [HttpGet]
    public IActionResult Get() => Ok(Payload);
}
