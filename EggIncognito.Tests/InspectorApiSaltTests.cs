using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace EggIncognito.Tests;

// Boots the real web host in-process and proves the Inspector build path is driven by a CLIENT-SUPPLIED
// salt (sent in the request body), not a server env var: a salt -> canSign true, no salt -> canSign
// false, the same salt -> a stable signature, and the old server-owned GET env-defaults endpoint is gone.
public class InspectorApiSaltTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public InspectorApiSaltTests(WebApplicationFactory<Program> f) =>
        _factory = f.WithWebHostBuilder(b => b.UseSetting("NoBrowser", "true"));

    // A non-empty body: the signing hash mutates a byte at a fixed offset, so the inner message must
    // serialize to at least one byte (an all-default {} message is zero bytes).
    private static object BuildBody(string? salt) => new
    {
        path = "ei/first_contact_secure",
        requestType = "EggIncFirstContactRequest",
        wrap = true,
        fields = new { eiUserId = "EI1234567890123456" },
        env = (object?)null,
        salt,
    };

    [Fact]
    public async Task Build_WithSalt_ReportsCanSignTrue()
    {
        var resp = await _factory.CreateClient().PostAsJsonAsync("/api/inspector/build", BuildBody("test-salt"));
        var bodyText = await resp.Content.ReadAsStringAsync();
        Assert.True(resp.IsSuccessStatusCode, $"status {(int)resp.StatusCode}: {bodyText}");
        using var doc = JsonDocument.Parse(bodyText);
        Assert.True(doc.RootElement.GetProperty("canSign").GetBoolean());
    }

    [Fact]
    public async Task Build_WithoutSalt_ReportsCanSignFalse()
    {
        var resp = await _factory.CreateClient().PostAsJsonAsync("/api/inspector/build", BuildBody(null));
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.False(doc.RootElement.GetProperty("canSign").GetBoolean());
    }

    [Fact]
    public async Task SameSalt_ProducesStableSignature()
    {
        var client = _factory.CreateClient();
        async Task<string> BuildOnce()
        {
            var resp = await client.PostAsJsonAsync("/api/inspector/build", BuildBody("stable-salt"));
            resp.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            return doc.RootElement.GetProperty("finalBase64").GetString()!;
        }
        Assert.Equal(await BuildOnce(), await BuildOnce());
    }

    [Fact]
    public async Task EnvDefaults_EndpointIsGone()
    {
        // The GET action was removed (defaults are now client-owned). No GET handler remains, so the
        // path no longer succeeds - it resolves to NotFound (no route) or MethodNotAllowed (a
        // catch-all claims the path for a different verb). Either proves the endpoint is gone.
        var resp = await _factory.CreateClient().GetAsync("/api/inspector/env-defaults");
        Assert.False(resp.IsSuccessStatusCode);
        Assert.Contains(resp.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed });
    }
}
