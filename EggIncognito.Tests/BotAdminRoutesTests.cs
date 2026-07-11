using System.Net;
using EggIncognito.Bot;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Npgsql;
using SyncKit.Bot;
using SyncKit.Contract;
using Xunit;

namespace EggIncognito.Tests;

// BotAdminRoutes.Map's isAdmin delegate is checked before anything touches ChannelConfigStore, so
// the denied/allowed paths are exercised here against a bare WebApplication + TestServer with a
// never-opened NpgsqlDataSource, no live Postgres needed.
public class BotAdminRoutesTests
{
    private static async Task<(WebApplication App, TestServer Server)> BuildMinimalApp(Func<HttpContext, bool> isAdmin)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        var app = builder.Build();

        var cfg = new BotConfig { Name = "EggIncognito", Token = "t", GuildId = "1", Build = new VerifyInfo() };
        // Never opened: denied requests never reach a store call, and the allowed test below
        // only hits GET /bot-admin (the static page), which also never touches the store.
        var dataSource = NpgsqlDataSource.Create("Host=localhost;Database=unused");
        var configStore = new ChannelConfigStore(dataSource);

        BotAdminRoutes.Map(app, cfg, configStore, isAdmin);

        await app.StartAsync();
        return (app, app.GetTestServer());
    }

    [Fact]
    public async Task Root_NotAdmin_Returns403()
    {
        var (app, server) = await BuildMinimalApp(_ => false);
        await using var _ = app;
        var client = server.CreateClient();

        var response = await client.GetAsync("/bot-admin");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ApiConfig_NotAdmin_Returns403()
    {
        var (app, server) = await BuildMinimalApp(_ => false);
        await using var _ = app;
        var client = server.CreateClient();

        var response = await client.GetAsync("/bot-admin/api/config");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Root_IsAdmin_ReturnsPage()
    {
        var (app, server) = await BuildMinimalApp(_ => true);
        await using var _ = app;
        var client = server.CreateClient();

        var response = await client.GetAsync("/bot-admin");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("EggIncognito Bot Config", await response.Content.ReadAsStringAsync());
    }
}
