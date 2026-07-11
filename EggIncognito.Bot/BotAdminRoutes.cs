using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SyncKit.Bot;

namespace EggIncognito.Bot;

// Vendored from SyncKit.Bot.AdminRoutes with paths remapped /admin -> /bot-admin (the bare /admin
// path collides with this app's own pre-existing Blazor admin console). Auth rides the app's own
// centralized login (SyncKit.Identity) instead of a second Discord OAuth flow: EggIncognito.Bot
// can't reference the web project's ICurrentUser directly (would be circular), so Program.cs passes
// in a per-request admin check delegate built against the real ICurrentUser/UserRole.Admin gate.
public static class BotAdminRoutes
{
    public static void Map(WebApplication app, BotConfig cfg, ChannelConfigStore configStore, Func<HttpContext, bool> isAdmin)
    {
        var admin = app.MapGroup("/bot-admin").AddEndpointFilter((efiContext, next) =>
        {
            var httpCtx = efiContext.HttpContext;
            if (!isAdmin(httpCtx)) return ValueTask.FromResult((object?)Results.StatusCode(StatusCodes.Status403Forbidden));
            return next(efiContext);
        });

        admin.MapGet("/", () => Results.Content(PageHtml, "text/html"));

        admin.MapGet("/api/config", async (HttpContext ctx) =>
        {
            var cc = await configStore.GetAsync(cfg.GuildId, cfg.Name, ctx.RequestAborted);
            return Results.Json(AdminConfigResponse.From(cc));
        });

        admin.MapPut("/api/config", async (HttpContext ctx, AdminConfigRequest req) =>
        {
            await configStore.UpsertAsync(cfg.GuildId, cfg.Name,
                req.DashboardChannelId, req.EnabledThreads,
                req.SuccessTemplate, req.FailureTemplate, req.AlreadyUpToDateTemplate,
                ctx.RequestAborted);
            return Results.Ok();
        });
    }

    private const string PageHtml = """
        <!doctype html>
        <html>
        <head>
        <meta charset="utf-8">
        <title>EggIncognito Bot Config</title>
        <style>
        body { font-family: system-ui, sans-serif; max-width: 720px; margin: 2rem auto; padding: 0 1rem; }
        label { display: block; margin-top: 1rem; font-weight: 600; }
        input, textarea { width: 100%; box-sizing: border-box; padding: 0.5rem; font-family: inherit; }
        textarea { min-height: 5rem; font-family: ui-monospace, monospace; }
        button { margin-top: 1.5rem; padding: 0.6rem 1.2rem; }
        #status { margin-left: 1rem; }
        </style>
        </head>
        <body>
        <h1>Deploy Notification Config</h1>
        <label for="channel">Dashboard channel ID</label>
        <input id="channel" type="text">
        <label for="threads">Enabled threads (CSV: GithubFeed, DeployNotifications)</label>
        <input id="threads" type="text">
        <label for="success">Success template (Scriban)</label>
        <textarea id="success"></textarea>
        <label for="failure">Failure template (Scriban)</label>
        <textarea id="failure"></textarea>
        <label for="uptodate">Already-up-to-date template (Scriban)</label>
        <textarea id="uptodate"></textarea>
        <button id="save">Save</button>
        <span id="status"></span>
        <script>
        async function load() {
          const r = await fetch('/bot-admin/api/config');
          const cfg = await r.json();
          document.getElementById('channel').value = cfg.dashboardChannelId || '';
          document.getElementById('threads').value = cfg.enabledThreads || '';
          document.getElementById('success').value = cfg.successTemplate || '';
          document.getElementById('failure').value = cfg.failureTemplate || '';
          document.getElementById('uptodate').value = cfg.alreadyUpToDateTemplate || '';
        }
        document.getElementById('save').addEventListener('click', async () => {
          const body = {
            dashboardChannelId: document.getElementById('channel').value || null,
            enabledThreads: document.getElementById('threads').value || null,
            successTemplate: document.getElementById('success').value || null,
            failureTemplate: document.getElementById('failure').value || null,
            alreadyUpToDateTemplate: document.getElementById('uptodate').value || null,
          };
          const r = await fetch('/bot-admin/api/config', { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
          document.getElementById('status').textContent = r.ok ? 'Saved.' : 'Save failed.';
        });
        load();
        </script>
        </body>
        </html>
        """;
}
