using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using EggIncognito.Services.ProtoExtract;
using EggIncognito.Tests.ProtoExtract;

namespace EggIncognito.Tests;

// Boots the real host and exercises the 3D-playground HTTP surface end to end: the page renders, the ship
// list endpoint responds, and the animate-glb toolkit endpoint round-trips a real .glb through SharpGLTF.
// Covers the wiring (routing, DI, multipart) that the GltfAnimator unit tests don't.
public class PlaygroundTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public PlaygroundTests(WebApplicationFactory<Program> f) =>
        _factory = f.WithWebHostBuilder(b => b.UseSetting("NoBrowser", "true"));

    [Fact]
    public async Task Playground_Page_Renders_AdminGated()
    {
        // Anonymous (no auth wired in the test host) sees the admin-required view, not the 3D canvas.
        var c = _factory.CreateClient();
        var r = await c.GetAsync("/playground");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var html = await r.Content.ReadAsStringAsync();
        Assert.Contains("3D Playground", html);
        Assert.Contains("Admin access required", html);
        Assert.DoesNotContain("playgroundCanvas", html);
    }

    [Fact]
    public async Task Devices_ListMeshes_RequiresAdmin()
    {
        var c = _factory.CreateClient();
        var r = await c.GetAsync("/api/devices/some-device/list-meshes");
        // Anonymous: admin gate returns 403 (or 503 when no DB), never 200 with data.
        Assert.True(r.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.ServiceUnavailable,
            $"expected 403/503, got {(int)r.StatusCode}");
    }

    [Fact]
    public async Task ShipAssets_List_Responds()
    {
        var c = _factory.CreateClient();
        var r = await c.GetAsync("/api/ship-assets/list");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        // No output dir configured in tests, so the list is empty but well-formed.
        var body = await r.Content.ReadFromJsonAsync<ListResult>();
        Assert.NotNull(body);
        Assert.NotNull(body!.Ships);
    }

    [Fact]
    public async Task AnimateGlb_RoundTripsThroughEndpoint()
    {
        var glb = RpoMeshDecoder.Decode(SampleRpo.Build(), "Endpoint").Glb!;

        var c = _factory.CreateClient();
        using var content = new MultipartFormDataContent();
        var part = new ByteArrayContent(glb);
        part.Headers.ContentType = new MediaTypeHeaderValue("model/gltf-binary");
        content.Add(part, "file", "ship.glb");

        var r = await c.PostAsync("/api/tools/animate-glb?kind=SpinY&seconds=5", content);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("model/gltf-binary", r.Content.Headers.ContentType?.MediaType);

        var outGlb = await r.Content.ReadAsByteArrayAsync();
        // It is a valid glb with an animation now.
        var model = SharpGLTF.Schema2.ModelRoot.ParseGLB(outGlb);
        Assert.Single(model.LogicalAnimations);
    }

    [Fact]
    public async Task Glb_UnknownShip_Is404()
    {
        var c = _factory.CreateClient();
        var r = await c.GetAsync("/api/ship-assets/glb/NotAShip");
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    [Fact]
    public async Task ShellsObjects_ReturnsShape()
    {
        var c = _factory.CreateClient();
        var r = await c.GetAsync("/api/shells/objects?platform=ios&type=chicken");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var json = await r.Content.ReadAsStringAsync();
        // public read; no stored config -> ok:false diagnostics, with one -> objects. either echoes type.
        Assert.Contains("\"ok\"", json);
        Assert.Contains("\"type\":\"chicken\"", json);
    }

    [Fact]
    public async Task EnvPresets_ReturnsPiecesAndPresets()
    {
        var c = _factory.CreateClient();
        var r = await c.GetAsync("/api/env/presets");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var json = await r.Content.ReadAsStringAsync();
        Assert.Contains("ei_farm_ground", json);
        Assert.Contains("\"presets\"", json);
    }

    [Fact]
    public async Task EnvPresets_IncludesHabs()
    {
        var c = _factory.CreateClient();
        var json = await (await c.GetAsync("/api/env/presets")).Content.ReadAsStringAsync();
        Assert.Contains("\"habs\"", json);
        Assert.Contains("hab_eggtopia", json);
    }

    [Fact]
    public async Task EnvGlb_RequiresAdmin()
    {
        // env meshes are pulled off a device (round-trip), so the glb route is admin-gated. Anonymous = 403,
        // never 200 with data. (No shipped assets: a 200 would mean a committed mesh, which must not exist.)
        var c = _factory.CreateClient();
        var r = await c.GetAsync("/api/env/ei_farm_ground/glb");
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task Config_List_Responds()
    {
        var c = _factory.CreateClient();
        var r = await c.GetAsync("/api/config");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [Fact]
    public async Task Config_Ingest_RequiresAdmin()
    {
        var c = _factory.CreateClient();
        var r = await c.PostAsJsonAsync("/api/config/ios/ingest", new { configResponseBase64 = "" });
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task InspectorBuild_ConfigRequest_WithClonedRinfo_Is200()
    {
        // The Admin "Fetch live via Inspector" path builds a ConfigRequest with the Inspector's rinfo defaults
        // as env (minus the UI-only "debug"). This must build, not 400 (the bug the thin/empty rinfo caused).
        var c = _factory.CreateClient();
        var body = new
        {
            path = "ei/get_config",
            requestType = "ConfigRequest",
            fields = new { },
            env = new { eiUserId = "EI5862923193024512", clientVersion = 72, version = "1.35.7", build = "111343", platform = "DROID", country = "US", language = "en" },
            wrap = true,
            salt = "",
        };
        var r = await c.PostAsJsonAsync("/api/inspector/build", body);
        var json = await r.Content.ReadAsStringAsync();
        Assert.True(r.StatusCode == HttpStatusCode.OK, $"HTTP {(int)r.StatusCode}: {json}");
        Assert.Contains("finalFormBody", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Config_IngestJson_RequiresAdmin()
    {
        var c = _factory.CreateClient();
        var r = await c.PostAsJsonAsync("/api/config/ios/ingest-json", new { json = "{}" });
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task Shells_List_Responds()
    {
        // No config stored in the test host, so ok=false with a diagnostic, but the route + parse work.
        var c = _factory.CreateClient();
        var r = await c.GetAsync("/api/shells?platform=ios");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [Fact]
    public async Task Precache_RequiresAdmin()
    {
        var c = _factory.CreateClient();
        var r = await c.PostAsync("/api/devices/x/precache-meshes", null);
        Assert.True(r.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task Console_Endpoints_RequiresAdmin()
    {
        var c = _factory.CreateClient();
        var r = await c.GetAsync("/api/console/endpoints");
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task Console_Page_Renders_AdminGated()
    {
        var c = _factory.CreateClient();
        var r = await c.GetAsync("/console");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var html = await r.Content.ReadAsStringAsync();
        Assert.Contains("API Console", html);
        Assert.Contains("Admin access required", html);
    }

    private record ListResult(string[]? Ships);
}
