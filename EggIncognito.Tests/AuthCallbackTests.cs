using System.Net;
using EggIdentity.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace EggIncognito.Tests;

public sealed class AuthCallbackFactory : EgiTestFactory {
    protected override void Configure(IWebHostBuilder builder) {
        builder.UseSetting("Identity:ApiUrl", "http://identity.local");
        builder.UseSetting("Identity:ApiSecret", "test-secret");
        builder.UseSetting("Identity:WidgetUrl", "http://identity.local");
        builder.ConfigureServices(s => s.AddSingleton(_ => StubIdentity()));
    }

    private static IdentityApiClient StubIdentity() {
        var uid = Guid.NewGuid();
        var http = new HttpClient(new StubHttpMessageHandler(req =>
                StubHttpMessageHandler.Json(HttpStatusCode.OK,
                    $$"""{"userId":"{{uid}}","username":"tester","role":"viewer","discordId":null,"avatar":null,"isNew":false}"""))) { BaseAddress = new Uri("http://identity.local") };
        return new IdentityApiClient(http);
    }
}

public class AuthCallbackTests(AuthCallbackFactory f) : IClassFixture<AuthCallbackFactory> {
    private HttpClient NoRedirectClient() =>
        f.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Fact]
    public async Task Code_OnAnyPage_SignsInAndRedirectsClean() {
        var c = NoRedirectClient();
        var r = await c.GetAsync("/protos?code=goodcode");
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Equal("/protos", r.Headers.Location?.OriginalString);
        Assert.Contains("egi.auth", string.Join(";", r.Headers.GetValues("Set-Cookie")));
    }

    [Fact]
    public async Task Code_PreservesOtherQueryParams() {
        var c = NoRedirectClient();
        var r = await c.GetAsync("/protos?tab=discord&code=goodcode");
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Equal("/protos?tab=discord", r.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Error_RedirectsWithLoginErrorFlag() {
        var c = NoRedirectClient();
        var r = await c.GetAsync("/?error=login_failed");
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Equal("/?login_error=1", r.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task NoAuthParams_PassesThrough() {
        var c = NoRedirectClient();
        var r = await c.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }
}
