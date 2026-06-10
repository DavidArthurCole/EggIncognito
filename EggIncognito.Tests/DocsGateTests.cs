using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace EggIncognito.Tests;

// /api/docs/* mirrors the shared-DB ACL: anonymous (= viewer in the test host) write actions 403;
// reads are public and degrade to empty/[] when no DB is configured. Same posture as
// StoredEndpointGateTests, applied to the docs + tag-assignment endpoints.
public class DocsGateTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public DocsGateTests(WebApplicationFactory<Program> f) =>
        _factory = f.WithWebHostBuilder(b => b
            .UseSetting("AppMode", "Hosted")
            .UseSetting("NoBrowser", "true"));

    [Fact]
    public async Task UpsertDoc_Anonymous_Is403()
    {
        var c = _factory.CreateClient();
        var r = await c.PostAsJsonAsync("/api/docs/doc",
            new { subjectKind = "message", subjectKey = "Contract", bodyMd = "# hi" });
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task SetSubjectTags_Anonymous_Is403()
    {
        var c = _factory.CreateClient();
        var r = await c.PostAsJsonAsync("/api/docs/subject-tags",
            new { subjectKind = "message", subjectKey = "Contract", tagIds = new long[] { 1 } });
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task AddTag_Anonymous_Is403()
    {
        var c = _factory.CreateClient();
        var r = await c.PostAsJsonAsync("/api/admin/tag", new { slug = "x", label = "X", color = (string?)null });
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task GetDoc_Reachable_EmptyWhenNoDb()
    {
        var c = _factory.CreateClient();
        var r = await c.GetAsync("/api/docs/doc/message/Contract");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode); // { bodyMd: null } with no DB
        Assert.Contains("bodyMd", await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task GetTags_Reachable_EmptyWhenNoDb()
    {
        var c = _factory.CreateClient();
        var r = await c.GetAsync("/api/docs/tags");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode); // [] with no DB
    }

    [Fact]
    public async Task GetDoc_InvalidKind_Is400()
    {
        var c = _factory.CreateClient();
        var r = await c.GetAsync("/api/docs/doc/bogus/Contract");
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task TagsMap_Reachable_EmptyWhenNoDb()
    {
        var c = _factory.CreateClient();
        var r = await c.GetAsync("/api/docs/tags-map");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [Fact]
    public async Task UploadImage_Anonymous_Is403()
    {
        var c = _factory.CreateClient();
        using var content = new MultipartFormDataContent();
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        var part = new ByteArrayContent(bytes);
        part.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        content.Add(part, "file", "x.png");
        var r = await c.PostAsync("/api/docs/image", content);
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode); // gate runs before DB resolve
    }

    [Fact]
    public async Task GetImage_NoDb_Is404()
    {
        var c = _factory.CreateClient();
        var r = await c.GetAsync("/api/docs/image/1");
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }
}
