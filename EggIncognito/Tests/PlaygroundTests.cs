using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;

namespace EggIncognito.Tests;

[Collection(SharedAppCollection.Name)]
public class PlaygroundTests(SharedAppFactory f) {
    private readonly WebApplicationFactory<Program> _factory = f;

    [Fact]
    public async Task Playground_Page_Renders_AdminGated() {
        var c = _factory.CreateClient();
        var r = await c.GetAsync("/playground");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        string html = await r.Content.ReadAsStringAsync();
        Assert.Contains("3D Playground", html);
        Assert.Contains("Admin access required", html);
        Assert.DoesNotContain("playgroundCanvas", html);
    }

    [Fact]
    public async Task Devices_Status_IsPublic_ForAnonymousHosts() {
        var c = _factory.CreateClient();
        var r = await c.GetAsync("/api/devices/status");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [Fact]
    public async Task Devices_ListMeshes_RequiresAdmin() {
        var c = _factory.CreateClient();
        var r = await c.GetAsync("/api/devices/some-device/list-meshes");
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task ShipAssets_Routes_AreGone() {
        var c = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/api/ship-assets/list")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/api/ship-assets/glb/ChickenOne")).StatusCode);
    }

    [Fact]
    public async Task MeshUploadTools_AreGone() {
        var c = _factory.CreateClient();
        using var empty = new MultipartFormDataContent();
        Assert.Equal(HttpStatusCode.NotFound,
            (await c.PostAsync("/api/tools/animate-glb?kind=SpinY&seconds=5", empty)).StatusCode);
        using var empty2 = new MultipartFormDataContent();
        Assert.Equal(HttpStatusCode.NotFound, (await c.PostAsync("/api/tools/extract-meshes", empty2)).StatusCode);
        using var empty3 = new MultipartFormDataContent();
        Assert.Equal(HttpStatusCode.NotFound, (await c.PostAsync("/api/tools/export-ships", empty3)).StatusCode);
    }

    [Fact]
    public async Task ShellsObjects_ReturnsShape() {
        var c = _factory.CreateClient();
        var r = await c.GetAsync("/api/shells/objects?platform=ios&type=chicken");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        string json = await r.Content.ReadAsStringAsync();

        Assert.Contains("\"ok\"", json);
        Assert.Contains("\"type\":\"chicken\"", json);
    }

    [Fact]
    public async Task PlaygroundRecorder_ScriptIsServed() {
        var c = _factory.CreateClient();
        var r = await c.GetAsync("/interop/playgroundRecorder.js");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        string body = await r.Content.ReadAsStringAsync();
        Assert.Contains("renderAtPhase", body);
        Assert.Contains("playground-loop.gif", body);
    }

    [Fact]
    public async Task FarmCatalog_Responds() {
        var c = _factory.CreateClient();
        var r = await c.GetAsync("/api/farm/catalog?platform=ios");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        string json = await r.Content.ReadAsStringAsync();
        Assert.Contains("\"ok\"", json);
        Assert.Contains("\"platform\":\"ios\"", json);
    }

    [Fact]
    public async Task FarmShowcase_ListsTheFixturePresets() {
        var c = _factory.CreateClient();
        var r = await c.GetAsync("/api/farm/showcase");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        string json = await r.Content.ReadAsStringAsync();
        Assert.Contains("\"ok\":true", json);
        Assert.Contains("\"count\":200", json);
    }

    [Fact]
    public async Task FarmLayout_WithoutInputs_ReportsWhatIsMissing() {
        var c = _factory.CreateClient();
        var r = await c.PostAsJsonAsync("/api/farm/layout", new { });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        string json = await r.Content.ReadAsStringAsync();
        Assert.Contains("\"ok\":false", json);
        Assert.Contains("\"diagnostics\"", json);
    }

    [Fact]
    public async Task FarmMesh_RejectsATraversalStem() {
        var c = _factory.CreateClient();
        var r = await c.GetAsync("/api/farm/mesh/..%2Fegginc");
        Assert.True(r.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound,
            $"expected the stem guard to reject, got {(int)r.StatusCode}");
    }

    [Fact]
    public async Task EnvRoutes_AreGone() {
        var c = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/api/env/catalog")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/api/env/farm-layout")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/api/env/device-stems")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/api/env/hatchery-effects")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/api/env/ei_farm_ground/glb")).StatusCode);
    }

    [Fact]
    public async Task EnvDesigns_List_RequiresContributor() {
        var c = _factory.CreateClient();
        var r = await c.GetAsync("/api/env/designs");
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task EnvDesigns_Read_RequiresContributor() {
        var c = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/api/env/designs/test")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/api/env/designs/test/versions")).StatusCode);
    }

    [Fact]
    public async Task EnvDesigns_VersionPayload_RouteIsGone() {
        var c = _factory.CreateClient();
        var r = await c.GetAsync("/api/env/designs/test/versions/1");
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    [Fact]
    public async Task EnvDesigns_Save_RequiresContributor() {
        var c = _factory.CreateClient();
        var body = new StringContent("{\"payload\":\"{}\"}", Encoding.UTF8, "application/json");
        var r = await c.PutAsync("/api/env/designs/test", body);
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task EnvDesigns_Delete_RequiresContributor() {
        var c = _factory.CreateClient();
        var r = await c.DeleteAsync("/api/env/designs/test");
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task Config_List_Responds() {
        var c = _factory.CreateClient();
        var r = await c.GetAsync("/api/config");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [Fact]
    public async Task Config_Ingest_RequiresAdmin() {
        var c = _factory.CreateClient();
        var r = await c.PostAsJsonAsync("/api/config/ios/ingest", new { configResponseBase64 = "" });
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task InspectorBuild_ConfigRequest_WithClonedRinfo_Is200() {
        var c = _factory.CreateClient();
        var body = new {
            path = "ei/get_config",
            requestType = "ConfigRequest",
            fields = new { },
            env = new {
                eiUserId = "EI5862923193024512",
                clientVersion = 72,
                version = "1.35.7",
                build = "111343",
                platform = "DROID",
                country = "US",
                language = "en"
            },
            wrap = true,
            salt = ""
        };
        var r = await c.PostAsJsonAsync("/api/inspector/build", body);
        string json = await r.Content.ReadAsStringAsync();
        Assert.True(r.StatusCode == HttpStatusCode.OK, $"HTTP {(int)r.StatusCode}: {json}");
        Assert.Contains("finalFormBody", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Config_IngestJson_RequiresAdmin() {
        var c = _factory.CreateClient();
        var r = await c.PostAsJsonAsync("/api/config/ios/ingest-json", new { json = "{}" });
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task Shells_List_Responds() {
        var c = _factory.CreateClient();
        var r = await c.GetAsync("/api/shells?platform=ios");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [Fact]
    public async Task Precache_RequiresAdmin() {
        var c = _factory.CreateClient();
        var r = await c.PostAsync("/api/devices/x/precache-meshes", null);
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task DeviceMeshRoutes_AreGone() {
        var c = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await c.PostAsync("/api/devices/x/pull-meshes", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/api/devices/x/mesh/ei_farm_ground")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/api/devices/x/cached-meshes")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await c.DeleteAsync("/api/devices/x/cached-meshes/ei_farm_ground")).StatusCode);
    }

    [Fact]
    public async Task Console_Endpoints_RequiresAdmin() {
        var c = _factory.CreateClient();
        var r = await c.GetAsync("/api/console/endpoints");
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task Periodicals_Route_MergedIntoProtosData_AdminOnlyTabs() {
        var c = _factory.CreateClient();
        var r = await c.GetAsync("/periodicals");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        string html = await r.Content.ReadAsStringAsync();
        Assert.Contains("Protos &amp; Data", html);

        Assert.DoesNotContain(">Periodicals</button>", html);
    }
}
