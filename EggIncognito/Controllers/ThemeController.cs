using EggIdentity.Contract;
using EggIncognito.Data.Services;
using EggIncognito.Models.Theme;
using EggIncognito.Services;
using EggIncognito.Services.Auth;
using EggIncognito.Services.Theme;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Caching.Memory;

namespace EggIncognito.Controllers;

[ApiController]
[Route("api/theme")]
[ApiAccess(ApiAccessLevel.Authenticated)]
public sealed class ThemeController(ICurrentUser currentUser, IServiceProvider services) : ControllerBase {
    private const int MaxBodyBytes = 64 * 1024;

    private UserThemeStore? Store => services.GetService(typeof(UserThemeStore)) as UserThemeStore;

    private ObjectResult? RequireContributor() =>
        currentUser.IsAtLeast(UserRole.Contributor)
            ? null
            : StatusCode(403, new { error = "contributor role required" });

    private ObjectResult? RequireAdmin() =>
        currentUser.IsAtLeast(UserRole.Admin)
            ? null
            : StatusCode(403, new { error = "admin role required" });

    private bool CustomCssConfigFloor() {
        var config = services.GetService(typeof(IConfiguration)) as IConfiguration;
        return config?.GetValue("Theme:CustomCss", true) ?? true;
    }

    private void InvalidateResolverCache() {
        if (currentUser.UserId is { } uid && services.GetService(typeof(IMemoryCache)) is IMemoryCache cache)
            ThemeResolver.Invalidate(cache, uid);
    }

    [HttpGet]
    [EnableRateLimiting("read")]
    public async Task<IActionResult> List() {
        if (currentUser.UserId is not { } uid) return Unauthorized(new { error = "login required" });
        var store = Store;
        if (store is null) return Ok(new { themes = Array.Empty<object>() });
        var rows = await store.ByOwnerAsync(uid, HttpContext.RequestAborted);
        return Ok(new {
            themes = rows.Select(t => new { t.Slug, t.Name, t.IsActive, t.SchemaVersion, t.UpdatedAt })
        });
    }

    [HttpGet("{slug}")]
    [EnableRateLimiting("read")]
    public async Task<IActionResult> Get(string slug) {
        if (currentUser.UserId is not { } uid) return Unauthorized(new { error = "login required" });
        var store = Store;
        if (store is null) return NotFound(new { error = "no database configured" });
        var row = await store.GetAsync(uid, slug, HttpContext.RequestAborted);
        return row is null ? NotFound(new { error = "unknown theme" }) : Content(row.Model, "application/json");
    }

    [HttpPut("{slug}")]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> Save(string slug) {
        if (currentUser.UserId is not { } uid) return Unauthorized(new { error = "login required" });
        var store = Store;
        if (store is null) return StatusCode(503, new { error = "no database configured" });
        string? json = await ReadBodyAsync();
        if (json is null) return BadRequest(new { error = "body too large" });
        var (model, errors) = ThemeModel.Parse(json);
        if (model is null) return BadRequest(new { error = "invalid theme", details = errors });
        if (!string.Equals(model.Slug, slug, StringComparison.Ordinal))
            return BadRequest(new { error = "slug in the body must match the route" });
        if (!string.IsNullOrEmpty(model.Css))
            return BadRequest(new { error = "custom css is saved via PUT /api/theme/css" });

        var existing = await store.GetAsync(uid, slug, HttpContext.RequestAborted);
        string keptCss = existing is not null ? ExtractCss(existing.Model) : "";
        var toStore = model with { Css = keptCss };
        var row = await store.UpsertAsync(uid, model.Slug, model.Name, model.SchemaVersion, toStore.ToJson(),
            HttpContext.RequestAborted);
        InvalidateResolverCache();
        return Ok(new { saved = row.Slug });
    }

    [HttpDelete("{slug}")]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> Delete(string slug) {
        if (currentUser.UserId is not { } uid) return Unauthorized(new { error = "login required" });
        var store = Store;
        if (store is null) return StatusCode(503, new { error = "no database configured" });
        bool deleted = await store.DeleteAsync(uid, slug, HttpContext.RequestAborted);
        if (!deleted) return NotFound(new { error = "unknown theme" });
        InvalidateResolverCache();
        return Ok(new { deleted = slug });
    }

    [HttpPost("{slug}/activate")]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> Activate(string slug) {
        if (currentUser.UserId is not { } uid) return Unauthorized(new { error = "login required" });
        var store = Store;
        if (store is null) return StatusCode(503, new { error = "no database configured" });
        var row = await store.GetAsync(uid, slug, HttpContext.RequestAborted);
        if (row is null) return NotFound(new { error = "unknown theme" });
        var (model, errors) = ThemeModel.Parse(row.Model);
        if (model is null) return UnprocessableEntity(new { error = "stored theme no longer parses", details = errors });
        var contrast = ThemeContrast.Validate(model);
        if (!contrast.Passes)
            return UnprocessableEntity(new { error = "contrast validation failed", failures = contrast.Failures });
        await store.ActivateAsync(uid, slug, System.Text.Json.JsonSerializer.Serialize(contrast),
            HttpContext.RequestAborted);
        InvalidateResolverCache();
        return Ok(new { activated = slug });
    }

    [HttpPost("deactivate")]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> Deactivate() {
        if (currentUser.UserId is not { } uid) return Unauthorized(new { error = "login required" });
        var store = Store;
        if (store is null) return StatusCode(503, new { error = "no database configured" });
        await store.DeactivateAsync(uid, HttpContext.RequestAborted);
        InvalidateResolverCache();
        return Ok(new { deactivated = true });
    }

    [HttpGet("{slug}/export")]
    [EnableRateLimiting("read")]
    public async Task<IActionResult> Export(string slug) {
        if (currentUser.UserId is not { } uid) return Unauthorized(new { error = "login required" });
        var store = Store;
        if (store is null) return NotFound(new { error = "no database configured" });
        var row = await store.GetAsync(uid, slug, HttpContext.RequestAborted);
        return row is null ? NotFound(new { error = "unknown theme" }) : Content(row.Model, "application/json");
    }

    [HttpPost("import")]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> Import() {
        if (currentUser.UserId is not { } uid) return Unauthorized(new { error = "login required" });
        var store = Store;
        if (store is null) return StatusCode(503, new { error = "no database configured" });
        string? json = await ReadBodyAsync();
        if (json is null) return BadRequest(new { error = "body too large" });
        var (model, errors) = ThemeModel.Parse(json);
        if (model is null) return BadRequest(new { error = "invalid theme", details = errors });
        if (!string.IsNullOrEmpty(model.Css)) {
            var parsed = ThemeCssParser.Parse(model.Css);
            if (!parsed.Ok) return BadRequest(new { error = "invalid custom css", details = parsed.Errors });
        }

        var row = await store.UpsertAsync(uid, model.Slug, model.Name, model.SchemaVersion, model.ToJson(),
            HttpContext.RequestAborted);
        InvalidateResolverCache();
        return Ok(new { imported = row.Slug });
    }

    [HttpPut("css")]
    [ApiAccess(ApiAccessLevel.Contributor)]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> SaveCss([FromBody] CssBody body) {
        if (RequireContributor() is { } no) return no;
        if (currentUser.UserId is not { } uid) return Unauthorized(new { error = "login required" });
        var store = Store;
        if (store is null) return StatusCode(503, new { error = "no database configured" });
        if (!CustomCssConfigFloor()) return StatusCode(403, new { error = "custom css is disabled by configuration" });
        var policy = await store.GetPolicyAsync(HttpContext.RequestAborted);
        if (!policy.CustomCssEnabled) return StatusCode(403, new { error = "custom css is disabled by the admin" });

        string css = body.Css ?? "";
        if (css.Length > 0) {
            var parsed = ThemeCssParser.Parse(css);
            if (!parsed.Ok) return BadRequest(new { error = "invalid custom css", details = parsed.Errors });
        }

        var row = await store.GetAsync(uid, body.Slug ?? "", HttpContext.RequestAborted);
        if (row is null) return NotFound(new { error = "unknown theme" });
        var (model, errors) = ThemeModel.Parse(row.Model);
        if (model is null) return UnprocessableEntity(new { error = "stored theme no longer parses", details = errors });
        var updated = model with { Css = css };
        await store.UpsertAsync(uid, model.Slug, model.Name, model.SchemaVersion, updated.ToJson(),
            HttpContext.RequestAborted);
        InvalidateResolverCache();
        return Ok(new { saved = model.Slug });
    }

    [HttpGet("policy")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> GetPolicy() {
        if (RequireAdmin() is { } no) return no;
        var store = Store;
        if (store is null) {
            return Ok(new {
                customCssEnabled = true,
                configFloor = CustomCssConfigFloor(),
                defaultThemeSlug = (string?)null
            });
        }

        var policy = await store.GetPolicyAsync(HttpContext.RequestAborted);
        string? defaultSlug = null;
        if (policy.DefaultThemeId is { } id)
            defaultSlug = (await store.GetByIdAsync(id, HttpContext.RequestAborted))?.Slug;
        return Ok(new {
            customCssEnabled = policy.CustomCssEnabled,
            configFloor = CustomCssConfigFloor(),
            defaultThemeSlug = defaultSlug
        });
    }

    [HttpPut("policy")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> SetPolicy([FromBody] PolicyBody body) {
        if (RequireAdmin() is { } no) return no;
        if (currentUser.UserId is not { } uid) return Unauthorized(new { error = "login required" });
        var store = Store;
        if (store is null) return StatusCode(503, new { error = "no database configured" });

        long? defaultThemeId = null;
        if (!string.IsNullOrWhiteSpace(body.DefaultThemeSlug)) {
            var theme = await store.GetAsync(uid, body.DefaultThemeSlug, HttpContext.RequestAborted);
            if (theme is null)
                return BadRequest(new { error = "the default theme must be one of your own themes" });
            if (ExtractCss(theme.Model).Length > 0)
                return BadRequest(new { error = "the default theme may not carry custom css" });
            defaultThemeId = theme.Id;
        }

        await store.SetPolicyAsync(body.CustomCssEnabled, defaultThemeId, uid, HttpContext.RequestAborted);
        return Ok(new { saved = true });
    }

    private async Task<string?> ReadBodyAsync() {
        using var reader = new StreamReader(Request.Body);
        string body = await reader.ReadToEndAsync(HttpContext.RequestAborted);
        return System.Text.Encoding.UTF8.GetByteCount(body) > MaxBodyBytes ? null : body;
    }

    private static string ExtractCss(string modelJson) {
        var (model, _) = ThemeModel.Parse(modelJson);
        return model?.Css ?? "";
    }
}
