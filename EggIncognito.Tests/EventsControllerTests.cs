using System.Net;
using System.Net.Http.Headers;

namespace EggIncognito.Tests;

public static class EventsControllerTests {
    private const string Body = "{\"package\":\"com.auxbrain.egginc\",\"version\":\"1.34\",\"protoSha\":\"x\"}";
    private static StringContent Json() => new(Body, System.Text.Encoding.UTF8, "application/json");

    [Collection(SharedAppCollection.Name)]
    public class NoSecret(SharedAppFactory f) {
        [Fact]
        public async Task NoSecretConfigured_Is404() {
            var r = await f.CreateClient().PostAsync("/events/new-version", Json());
            Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        }
    }

    [Collection(EventSecretAppCollection.Name)]
    public class WithSecret(EventSecretAppFactory f) {
        [Fact]
        public async Task NoBearer_Is401() {
            var r = await f.CreateClient().PostAsync("/events/new-version", Json());
            Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        }

        [Fact]
        public async Task WrongBearer_Is401() {
            var req = new HttpRequestMessage(HttpMethod.Post, "/events/new-version") { Content = Json() };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "wrong");
            var r = await f.CreateClient().SendAsync(req);
            Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        }

        [Fact]
        public async Task RightBearer_Is200() {
            var req = new HttpRequestMessage(HttpMethod.Post, "/events/new-version") { Content = Json() };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", EventSecretAppFactory.Secret);
            var r = await f.CreateClient().SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        }
    }
}
