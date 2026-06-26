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
    public async Task Playground_Page_Renders()
    {
        var c = _factory.CreateClient();
        var r = await c.GetAsync("/playground");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var html = await r.Content.ReadAsStringAsync();
        Assert.Contains("playgroundCanvas", html);
        Assert.Contains("3D Playground", html);
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

    private record ListResult(string[]? Ships);
}
