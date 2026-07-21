using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using EggIncognito.Services.ProtoExtract;
using EggIncognito.Tests.ProtoExtract;

namespace EggIncognito.Tests;


[Collection(SharedAppCollection.Name)]
public class PlaygroundTests
{
    private readonly WebApplicationFactory<Program> _factory;

    public PlaygroundTests(SharedAppFactory f) => _factory = f;

    [Fact]
    public async Task Playground_Page_Renders_AdminGated()
    {
       
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

        Assert.True(r.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.ServiceUnavailable,
            $"expected 401/403/503, got {(int)r.StatusCode}");
    }

    [Fact]
    public async Task ShipAssets_List_Responds()
    {
        var c = _factory.CreateClient();
        var r = await c.GetAsync("/api/ship-assets/list");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
       
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
       
        Assert.Contains("\"ok\"", json);
        Assert.Contains("\"type\":\"chicken\"", json);
    }

    [Fact]
    public async Task PlaygroundRecorder_ScriptIsServed()
    {
        var c = _factory.CreateClient();
        var r = await c.GetAsync("/interop/playgroundRecorder.js");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var body = await r.Content.ReadAsStringAsync();
        Assert.Contains("renderAtPhase", body);
        Assert.Contains("playground-loop.gif", body);
    }

    [Fact]
    public async Task EnvCatalog_ReturnsPiecesAndHabs()
    {
        var c = _factory.CreateClient();
        var r = await c.GetAsync("/api/env/catalog");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var json = await r.Content.ReadAsStringAsync();
        Assert.Contains("ei_silo_0_large", json);
        Assert.Contains("\"habs\"", json);
        Assert.Contains("hab_eggtopia", json);
        Assert.DoesNotContain("\"presets\"", json);
    }

    [Fact]
    public async Task EnvDesigns_List_PublicReturnsArrayShape()
    {
        var c = _factory.CreateClient();
        var r = await c.GetAsync("/api/env/designs");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Contains("designs", await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task EnvDesigns_Save_RequiresContributor()
    {
        var c = _factory.CreateClient();
        var body = new StringContent("{\"payload\":\"{}\"}", System.Text.Encoding.UTF8, "application/json");
        var r = await c.PutAsync("/api/env/designs/test", body);
       
        Assert.True(r.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.ServiceUnavailable,
            $"expected 403/503, got {(int)r.StatusCode}");
    }

    [Fact]
    public async Task EnvDesigns_Delete_RequiresContributor()
    {
        var c = _factory.CreateClient();
        var r = await c.DeleteAsync("/api/env/designs/test");
        Assert.True(r.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.ServiceUnavailable,
            $"expected 403/503, got {(int)r.StatusCode}");
    }

    [Fact]
    public async Task EnvGlb_RequiresAdmin()
    {
       
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
       
        var c = _factory.CreateClient();
        var r = await c.GetAsync("/api/shells?platform=ios");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [Fact]
    public async Task Precache_RequiresAdmin()
    {
        var c = _factory.CreateClient();
        var r = await c.PostAsync("/api/devices/x/precache-meshes", null);
        Assert.True(r.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task Console_Endpoints_RequiresAdmin()
    {
        var c = _factory.CreateClient();
        var r = await c.GetAsync("/api/console/endpoints");
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task Periodicals_Page_Renders_AdminGated()
    {
        var c = _factory.CreateClient();
        var r = await c.GetAsync("/periodicals");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var html = await r.Content.ReadAsStringAsync();
        Assert.Contains("Periodicals", html);
        Assert.Contains("Admin access required", html);
    }

    private record ListResult(string[]? Ships);
}
