using Microsoft.AspNetCore.Mvc;
using EggIncognito.Services;

namespace EggIncognito.Controllers;

[ApiController]
public class SimulationController(IBehaviorService behaviors) : ControllerBase
{
    [HttpOptions("/")]
    public IActionResult GetAll() => Ok(behaviors.All());

    [HttpOptions("/{**slug}")]
    public IActionResult GetForSlug(string slug) => Ok(behaviors.ForEndpoint(slug));
}
