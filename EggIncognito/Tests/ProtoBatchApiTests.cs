using System.Net;
using EggIdentity.Contract;
using EggIncognito.Controllers;
using EggIncognito.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace EggIncognito.Tests;

[Collection(SharedAppCollection.Name)]
public class ProtoBatchApiTests(SharedAppFactory f) {
    private readonly WebApplicationFactory<Program> _factory = f;

    private static ProtoBatchController Controller(
        UserRole role, bool supporter = false, bool authenticated = true, string? discordId = "u1") =>
        new(new EmptyServices(), new FakeUser(role, supporter, authenticated, discordId), Config());

    private static IConfiguration Config(int maxFiles = 50) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["BatchUpload:MaxFiles"] = maxFiles.ToString()
        }).Build();

    private static int Status(IActionResult r) => ((IStatusCodeActionResult)r).StatusCode ?? 200;

    private static IFormFileCollection MakeFiles(int count, int bytesEach = 3) {
        var coll = new FormFileCollection();
        for (int i = 0; i < count; i++) {
            byte[] data = new byte[bytesEach];
            var stream = new MemoryStream(data);
            coll.Add(new FormFile(stream, 0, data.Length, "files", $"f{i}.apk"));
        }
        return coll;
    }

    [Fact]
    public async Task Viewer_Upload_Is403() {
        var r = await Controller(UserRole.Viewer).Upload(MakeFiles(1), CancellationToken.None);
        Assert.Equal(403, Status(r));
    }

    [Fact]
    public async Task Contributor_TooManyFiles_Is400() {
        var r = await Controller(UserRole.Contributor).Upload(MakeFiles(51), CancellationToken.None);
        Assert.Equal(400, Status(r));
    }

    [Fact]
    public async Task Anonymous_Upload_Is401_ViaApiAccessFloor() {
        using var c = _factory.CreateClient();
        using var content = new MultipartFormDataContent();
        var r = await c.PostAsync("/api/protos/batch", content);
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact(Skip = "needs a reachable Postgres")]
    public async Task Supporter_Upload_OneFile_Is200() {
        var r = await Controller(UserRole.Viewer, supporter: true).Upload(MakeFiles(1), CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(r);
        Assert.NotNull(ok.Value);
    }

    [Fact(Skip = "needs a reachable Postgres")]
    public async Task Contributor_Upload_OneFile_Is200() {
        var r = await Controller(UserRole.Contributor).Upload(MakeFiles(1), CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(r);
        Assert.NotNull(ok.Value);
    }

    [Fact(Skip = "needs a reachable Postgres")]
    public async Task Owner_Get_Is200() {
        var r = await Controller(UserRole.Contributor).Get(1, CancellationToken.None);
        Assert.IsType<OkObjectResult>(r);
    }

    private sealed class EmptyServices : IServiceProvider {
        public object? GetService(Type serviceType) => null;
    }

    private sealed class FakeUser(
        UserRole role, bool supporter = false, bool authenticated = true, string? discordId = "u1") : ICurrentUser {
        public bool IsAuthenticated => authenticated;
        public Guid? UserId => null;
        public string? DiscordId => discordId;
        public string? Username => "u";
        public string? Avatar => null;
        public string? AvatarUrl => null;
        public UserRole Role => role;
        public bool IsSupporter => supporter;
        public bool IsAtLeast(UserRole need) => UserRoles.IsAtLeast(role, need);
    }
}
