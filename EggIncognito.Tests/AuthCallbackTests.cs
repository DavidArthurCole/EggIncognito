using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using SyncKit.Identity.Client;
using Xunit;

namespace EggIncognito.Tests;

// Widget-on integration: /auth/callback redeems ?code into the auth cookie and redirects; ?error and
// failures land on /?login_error=1. Identity API is stubbed so no live SyncKit host is needed.
public class AuthCallbackTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public AuthCallbackTests(WebApplicationFactory<Program> f)
    {
        _factory = f.WithWebHostBuilder(b =>
        {
            b.UseSetting("NoBrowser", "true");
            b.UseSetting("Identity:ApiUrl", "http://identity.local");
            b.UseSetting("Identity:ApiSecret", "test-secret");
            b.UseSetting("Identity:WidgetUrl", "http://identity.local");
            b.ConfigureServices(s =>
            {
                s.AddSingleton(_ => StubIdentity());
            });
        });
    }

    private static IdentityApiClient StubIdentity()
    {
        var uid = Guid.NewGuid();
        var http = new HttpClient(new StubHttpMessageHandler(req =>
            StubHttpMessageHandler.Json(HttpStatusCode.OK,
                $$"""{"userId":"{{uid}}","username":"tester","role":"viewer","discordId":null,"avatar":null,"isNew":false}""")))
        { BaseAddress = new Uri("http://identity.local") };
        return new IdentityApiClient(http);
    }

    private HttpClient NoRedirectClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Fact]
    public async Task Callback_ValidCode_SignsInAndRedirects()
    {
        var c = NoRedirectClient();
        var r = await c.GetAsync("/auth/callback?code=goodcode");
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Contains("egi.auth", string.Join(";", r.Headers.GetValues("Set-Cookie")));
    }

    [Fact]
    public async Task Callback_Error_RedirectsToLoginError()
    {
        var c = NoRedirectClient();
        var r = await c.GetAsync("/auth/callback?error=login_failed");
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Equal("/?login_error=1", r.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Callback_MissingCode_400()
    {
        var c = NoRedirectClient();
        var r = await c.GetAsync("/auth/callback");
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Callback_ValidCode_RedirectsToStashedReturnPath()
    {
        var c = NoRedirectClient();
        var set = await c.PostAsync("/auth/login-return", new StringContent("\"/admin\""));
        Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        var cookie = set.Headers.GetValues("Set-Cookie")
            .First(h => h.StartsWith("egi.login_return"));
        var value = cookie.Split(';')[0];

        var req = new HttpRequestMessage(HttpMethod.Get, "/auth/callback?code=goodcode");
        req.Headers.Add("Cookie", value);
        var r = await c.SendAsync(req);
        Assert.Equal("/admin", r.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task LoginReturn_RejectsNonLocalPath()
    {
        var c = NoRedirectClient();
        var r = await c.PostAsync("/auth/login-return", new StringContent("\"https://evil.example.com/x\""));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var cookie = r.Headers.GetValues("Set-Cookie").First(h => h.StartsWith("egi.login_return"));
        Assert.StartsWith("egi.login_return=%2F;", cookie);
    }
}
