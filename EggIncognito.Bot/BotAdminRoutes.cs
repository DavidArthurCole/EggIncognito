using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SyncKit.Auth;
using SyncKit.Bot;

namespace EggIncognito.Bot;

// Vendored from SyncKit.Bot.AdminRoutes with paths remapped /admin -> /bot-admin (the bare /admin
// path collides with this app's own pre-existing Blazor admin console) and the client resolved
// through BotInstanceHolder since routes are mapped before the bot hosted service has started.
public static class BotAdminRoutes
{
    private static readonly Dictionary<string, byte> PendingStates = [];

    public static void Map(WebApplication app, BotConfig cfg, ChannelConfigStore configStore, AdminSessionStore sessionStore, BotInstanceHolder botHolder)
    {
        app.MapGet("/bot-admin/login", () =>
        {
            var (url, state) = DiscordOAuth.AuthUrl();
            lock (PendingStates) PendingStates[state] = 0;
            return Results.Redirect(url);
        });

        app.MapGet("/bot-admin/callback", async (HttpContext ctx, string code, string state) =>
        {
            lock (PendingStates)
            {
                if (!PendingStates.Remove(state))
                    return Results.Text("invalid or expired login attempt", "text/plain", null, StatusCodes.Status400BadRequest);
            }

            string? sessionToken = null;
            DiscordUser? discordUser = null;
            await DiscordOAuth.HandleCallbackAsync(code, state, (_, token, user) =>
            {
                sessionToken = token;
                discordUser = user;
                return Task.CompletedTask;
            }, ctx.RequestAborted);

            if (sessionToken is null || discordUser is null)
                return Results.Text("login failed", "text/plain", null, StatusCodes.Status400BadRequest);

            var bot = botHolder.Bot;
            if (bot is null) return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);

            if (!IsGuildAdmin(bot.Client, cfg.GuildId, discordUser.Id))
                return Results.Text("not a guild administrator", "text/plain", null, StatusCodes.Status403Forbidden);

            var expiresAt = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds();
            await sessionStore.CreateAsync(sessionToken, discordUser.Id, expiresAt, ctx.RequestAborted);

            ctx.Response.Cookies.Append("egi_bot_admin_session", sessionToken, new CookieOptions { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Lax, MaxAge = TimeSpan.FromDays(30) });
            return Results.Redirect("/bot-admin");
        });

        app.MapPost("/bot-admin/logout", async (HttpContext ctx) =>
        {
            if (ctx.Request.Cookies.TryGetValue("egi_bot_admin_session", out var token) && !string.IsNullOrEmpty(token))
                await sessionStore.DeleteAsync(token, ctx.RequestAborted);
            ctx.Response.Cookies.Delete("egi_bot_admin_session");
            return Results.Redirect("/bot-admin/login");
        });

        var admin = app.MapGroup("/bot-admin").AddEndpointFilter(async (efiContext, next) =>
        {
            var httpCtx = efiContext.HttpContext;
            if (httpCtx.Request.Path.Value is "/bot-admin/login" or "/bot-admin/callback") return await next(efiContext);

            if (!httpCtx.Request.Cookies.TryGetValue("egi_bot_admin_session", out var token) || string.IsNullOrEmpty(token))
                return (object?)Results.Redirect("/bot-admin/login");

            var (found, discordId, expiresAt) = await sessionStore.LookupAsync(token, httpCtx.RequestAborted);
            var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (!found || nowUnix > expiresAt)
                return (object?)Results.Redirect("/bot-admin/login");

            var bot = botHolder.Bot;
            if (bot is null) return (object?)Results.StatusCode(StatusCodes.Status503ServiceUnavailable);

            if (!IsGuildAdmin(bot.Client, cfg.GuildId, discordId))
                return (object?)Results.Text("not authorized", "text/plain", null, StatusCodes.Status403Forbidden);

            var slid = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds();
            await sessionStore.TouchAsync(token, slid, httpCtx.RequestAborted);
            httpCtx.Items["DiscordId"] = discordId;
            return await next(efiContext);
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

    private static bool IsGuildAdmin(Discord.WebSocket.DiscordSocketClient client, string guildIdStr, string discordId)
    {
        if (!ulong.TryParse(guildIdStr, out var guildId) || !ulong.TryParse(discordId, out var userId)) return false;
        var guild = client.GetGuild(guildId);
        var member = guild?.GetUser(userId);
        return member is not null && member.GuildPermissions.Administrator;
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
