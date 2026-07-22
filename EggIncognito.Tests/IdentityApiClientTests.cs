using System.Net;
using SyncKit.Identity.Client;

namespace EggIncognito.Tests;

public class IdentityApiClientTests {
    private static IdentityApiClient Client(Func<HttpRequestMessage, HttpResponseMessage> respond) {
        var http = new HttpClient(new StubHttpMessageHandler(respond)) { BaseAddress = new Uri("http://identity.local") };
        return new IdentityApiClient(http);
    }

    [Fact]
    public async Task ResolveAsync_PostsAndParsesResponse() {
        var userId = Guid.NewGuid();
        var client = Client(req => {
            Assert.Equal("/identity/resolve", req.RequestUri!.AbsolutePath);
            return StubHttpMessageHandler.Json(HttpStatusCode.OK,
                $$"""{"userId":"{{userId}}","role":"viewer","discordId":"123","isNew":true}""");
        });

        var result = await client.ResolveAsync("discord", "123", "123", "alice", null, CancellationToken.None);

        Assert.Equal(userId, result.UserId);
        Assert.Equal("viewer", result.Role);
        Assert.True(result.IsNew);
    }

    [Fact]
    public async Task ListAdminUsersAsync_ParsesArray() {
        var client = Client(req => {
            Assert.Equal("/identity/admin/users", req.RequestUri!.AbsolutePath);
            return StubHttpMessageHandler.Json(HttpStatusCode.OK,
                """[{"userId":"11111111-1111-1111-1111-111111111111","discordId":"123","username":"alice","role":"admin","createdAt":"2026-01-01T00:00:00Z","lastLoginAt":"2026-01-01T00:00:00Z"}]""");
        });

        var users = await client.ListAdminUsersAsync(CancellationToken.None);

        Assert.Single(users);
        Assert.Equal("alice", users[0].Username);
        Assert.Equal("admin", users[0].Role);
    }

    [Fact]
    public async Task RevokeSessionAsync_PostsSidOnly_NoUserId() {
        var called = false;
        var client = Client(req => {
            called = true;
            Assert.Equal("/identity/revoke-session", req.RequestUri!.AbsolutePath);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        await client.RevokeSessionAsync("sid-1", CancellationToken.None);

        Assert.True(called);
    }

    [Fact]
    public async Task IsRevokedAsync_ParsesBoolBody() {
        var client = Client(req => {
            Assert.Equal("/identity/sessions/sid-1/revoked", req.RequestUri!.AbsolutePath);
            return StubHttpMessageHandler.Json(HttpStatusCode.OK, "true");
        });

        var revoked = await client.IsRevokedAsync("sid-1", CancellationToken.None);

        Assert.True(revoked);
    }

    [Fact]
    public async Task SetRoleAsync_PostsToUserRoleRoute() {
        var userId = Guid.NewGuid();
        var client = Client(req => {
            Assert.Equal($"/identity/{userId}/role", req.RequestUri!.AbsolutePath);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        await client.SetRoleAsync(userId, "admin", CancellationToken.None);
    }
}
