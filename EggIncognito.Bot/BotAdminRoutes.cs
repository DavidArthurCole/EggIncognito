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
        <link rel="stylesheet" href="/tailwind.css">
        <style>
        /* Raw token values (see tailwind.config.js): this page's markup lives in EggIncognito.Bot,
           outside the paths Tailwind's content scanner reads, so only .panel/.btn-primary (compiled
           via @layer components, immune to purge) are safe to reference by class here. Everything
           else uses the literal hex values instead of utility classes that could be purged. */
        body { margin: 0; background: #1b1b1f; color: #e7e7ea; font-family: system-ui, sans-serif; }
        .bot-admin-page { max-width: 42rem; margin: 2rem auto; }
        .bot-admin-field { display: block; margin-top: 1rem; }
        .bot-admin-field label { display: block; margin-bottom: 0.35rem; font-weight: 600; }
        .bot-admin-field input, .bot-admin-field textarea {
          width: 100%; box-sizing: border-box; padding: 0.5rem; font-family: inherit;
          background: #2e2e36; color: #e7e7ea; border: 1px solid #3a3a44; border-radius: 0.375rem;
        }
        .bot-admin-field textarea { min-height: 5rem; font-family: ui-monospace, monospace; }
        .bot-admin-actions { margin-top: 1.5rem; display: flex; align-items: center; gap: 0.75rem; }
        </style>
        </head>
        <body>
        <div class="panel bot-admin-page">
        <h2>Deploy Notification Config</h2>
        <div class="bot-admin-field">
          <label for="channel">Dashboard channel ID</label>
          <input id="channel" type="text">
        </div>
        <div class="bot-admin-field">
          <label for="threads">Enabled threads (CSV: GithubFeed, DeployNotifications)</label>
          <input id="threads" type="text">
        </div>
        <div class="bot-admin-field">
          <label for="success">Success template (Scriban)</label>
          <textarea id="success"></textarea>
        </div>
        <div class="bot-admin-field">
          <label for="failure">Failure template (Scriban)</label>
          <textarea id="failure"></textarea>
        </div>
        <div class="bot-admin-field">
          <label for="uptodate">Already-up-to-date template (Scriban)</label>
          <textarea id="uptodate"></textarea>
        </div>
        <div class="bot-admin-actions">
          <button id="save" class="btn-primary">Save</button>
          <span id="status"></span>
        </div>
        </div>
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
