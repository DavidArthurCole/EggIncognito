using System.Net;
using System.Reflection;
using System.Text.Json;
using Google.Protobuf;
using Microsoft.AspNetCore.RateLimiting;

namespace EggIncognito.Tests;

public class AuxbrainDropInTests(EggIncApiFactory factory) : IClassFixture<EggIncApiFactory> {
    private readonly HttpClient _client = factory.CreateClient();

    private static FormUrlEncodedContent EmptyForm() {
        var data = Convert.ToBase64String(new Ei.AuthenticatedMessage().ToByteArray());
        return new FormUrlEncodedContent([new KeyValuePair<string, string>("data", data)]);
    }

    [Fact]
    public async Task KnownMockedPath_StillServesCannedProto() {
        var resp = await _client.PostAsync("/ei/first_contact_secure", EmptyForm());
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadAsStringAsync();
        var result = Ei.EggIncFirstContactResponse.Parser.ParseFrom(Convert.FromBase64String(body));
        Assert.Equal("EI0000000000000001", result.EiUserId);
    }

    [Fact]
    public async Task UnmappedPath_InKnownNamespace_Returns200Marker_Never404() {
        var resp = await _client.PostAsync("/ei/zz_totally_unknown", EmptyForm());
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("not-mocked", resp.Headers.GetValues("x-eggincognito").Single());
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.Equal("ei/zz_totally_unknown", doc.RootElement.GetProperty("path").GetString());
    }

    [Fact]
    public async Task UnmockedButTypedCanonicalPath_ReturnsEmptyProto() {
        var resp = await _client.PostAsync("/ei/coop_status_bot", EmptyForm());
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.False(resp.Headers.Contains("x-eggincognito"));
        Assert.Equal("text/html", resp.Content.Headers.ContentType?.MediaType);
        var body = await resp.Content.ReadAsStringAsync();
        var msg = Ei.ContractCoopStatusResponse.Parser.ParseFrom(Convert.FromBase64String(body));
        Assert.Equal(new Ei.ContractCoopStatusResponse(), msg);
    }

    [Fact]
    public async Task AliasPath_ServesSameBodyAsCanonicalPath() {
        var canonical = await _client.PostAsync("/ei/update_coop_status", EmptyForm());
        var alias = await _client.PostAsync("/ei/update_coop_status_secure", EmptyForm());
        Assert.Equal(HttpStatusCode.OK, alias.StatusCode);
        Assert.False(alias.Headers.Contains("x-eggincognito"));
        Assert.Equal("text/html", alias.Content.Headers.ContentType?.MediaType);

        var canonicalBody = await canonical.Content.ReadAsStringAsync();
        var aliasBody = await alias.Content.ReadAsStringAsync();
        Assert.Equal(canonicalBody, aliasBody);

        var msg = Ei.ContractCoopStatusUpdateResponse.Parser.ParseFrom(Convert.FromBase64String(aliasBody));
        Assert.True(msg.Finalized);
        Assert.True(msg.Exists);
    }

    [Fact]
    public async Task OpenApiJson_Parses_AndAgreesWithCatalogPaths() {
        var openApiResp = await _client.GetAsync("/api/openapi.json");
        openApiResp.EnsureSuccessStatusCode();
        using var openApi = JsonDocument.Parse(await openApiResp.Content.ReadAsStringAsync());
        Assert.Equal("3.0.3", openApi.RootElement.GetProperty("openapi").GetString());
        var paths = openApi.RootElement.GetProperty("paths");

        var catalogResp = await _client.GetAsync("/api/catalog");
        catalogResp.EnsureSuccessStatusCode();
        using var catalog = JsonDocument.Parse(await catalogResp.Content.ReadAsStringAsync());
        var entries = catalog.RootElement.EnumerateArray().ToList();
        Assert.True(entries.Count >= 64, $"expected >= 64 catalog entries, got {entries.Count}");

        foreach (var entry in entries) {
            var path = entry.GetProperty("path").GetString()!;
            Assert.True(paths.GetProperty("/" + path).TryGetProperty("post", out _),
                $"catalog path {path} missing from openapi.json");
        }
    }

    [Fact]
    public async Task NamespaceIndex_Ei_ListsOnlyEiRoutes_WithStatusLabels() {
        var resp = await _client.GetAsync("/ei");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.Equal("ei", doc.RootElement.GetProperty("namespace").GetString());

        var routes = doc.RootElement.GetProperty("routes").EnumerateArray().ToList();
        Assert.NotEmpty(routes);
        string[] labels = ["ok", "empty", "missing", "not-mocked"];
        foreach (var r in routes) {
            Assert.StartsWith("ei/", r.GetProperty("path").GetString());
            Assert.Contains(r.GetProperty("status").GetString(), labels);
        }
    }

    [Theory]
    [InlineData("/api")]
    [InlineData("/api/reference")]
    public async Task LandingAndReference_Return200Html(string url) {
        var resp = await _client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("text/html", resp.Content.Headers.ContentType?.MediaType);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("EggIncognito", body);
    }

    [Fact]
    public async Task Landing_DocumentsFormShape_SigningAndInspector() {
        var body = await _client.GetStringAsync("/api");
        Assert.Contains("data=", body);
        Assert.Contains("AuthenticatedMessage", body);
        Assert.Contains("/api/catalog", body);
        Assert.Contains("/inspector", body);
    }

    [Fact]
    public void ApiSurfaceController_HasReadRateLimitPolicy() {
        var attr = typeof(EggIncognito.Controllers.ApiSurfaceController)
            .GetCustomAttribute<EnableRateLimitingAttribute>();
        Assert.NotNull(attr);
        Assert.Equal("read", attr!.PolicyName);
    }
}
