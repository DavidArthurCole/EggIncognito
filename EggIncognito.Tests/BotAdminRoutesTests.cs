using System.Net;
using EggIncognito.Bot;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Npgsql;
using SyncKit.Auth;
using SyncKit.Bot;
using SyncKit.Contract;
using Xunit;

namespace EggIncognito.Tests;

// BotAdminRoutes.Map only touches Postgres once a request passes the cookie check (ChannelConfigStore/
// AdminSessionStore are real Npgsql-backed stores with no test double in this repo). The login redirect
// and the no-cookie gate never reach a store call, so they're exercised here against a bare WebApplication
// + TestServer instead of the full Program.cs/WebApplicationFactory, which needs a live Postgres just to
// pass the botAdminEnabled gate. The 503-when-bot-null case needs a real authenticated session row, so it
// needs the full app and a real DB; skipped per the repo's existing DB-test convention (see README.md).
public class BotAdminRoutesTests
{
    private static async Task<(WebApplication App, TestServer Server)> BuildMinimalApp()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        var app = builder.Build();

        var cfg = new BotConfig { Name = "EggIncognito", Token = "t", GuildId = "1", Build = new VerifyInfo() };
        // Never opened: the routes under test here never reach a store call.
        var dataSource = NpgsqlDataSource.Create("Host=localhost;Database=unused");
        var configStore = new ChannelConfigStore(dataSource);
        var sessionStore = new AdminSessionStore(dataSource);
        var botHolder = new BotInstanceHolder();

        DiscordOAuth.Init("test-client-id", "test-client-secret", "https://example.test/bot-admin/callback");
        BotAdminRoutes.Map(app, cfg, configStore, sessionStore, botHolder);

        await app.StartAsync();
        return (app, app.GetTestServer());
    }

    [Fact]
    public async Task Root_NoSessionCookie_RedirectsToLogin()
    {
        var (app, server) = await BuildMinimalApp();
        await using var _ = app;
        var client = server.CreateClient();

        var response = await client.GetAsync("/bot-admin");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/bot-admin/login", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Login_RedirectsToDiscordAuthorizeUrl_WithState()
    {
        var (app, server) = await BuildMinimalApp();
        await using var _ = app;
        var client = server.CreateClient();

        var response = await client.GetAsync("/bot-admin/login");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location;
        Assert.NotNull(location);
        Assert.StartsWith("https://discord.com/api/oauth2/authorize", location!.OriginalString);
        Assert.Contains("state=", location.Query);
    }

    [Fact]
    public async Task ApiConfig_NoSessionCookie_MatchesGroupFilterGate()
    {
        var (app, server) = await BuildMinimalApp();
        await using var _ = app;
        var client = server.CreateClient();

        var response = await client.GetAsync("/bot-admin/api/config");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/bot-admin/login", response.Headers.Location?.OriginalString);
    }

    [Fact(Skip = "requires Postgres; no EF test provider per tests-DB-free repo rule")]
    public Task Root_ValidSessionButBotNotStarted_Returns503()
    {
        // Needs botAdminEnabled=true in Program.cs (real ConnectionStrings:Postgres plus the three
        // Discord:BotAdminClientId/Secret/CallbackUrl keys) and a real admin_sessions row so the group
        // filter's sessionStore.LookupAsync succeeds and falls through to the botHolder.Bot null check.
        // No fake/in-memory Postgres substitute exists in this repo for AdminSessionStore; run manually
        // against a live Postgres (eggincognito_test) with BotInstanceHolder.Bot left unset.
        return Task.CompletedTask;
    }
}
