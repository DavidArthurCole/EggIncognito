using System.Net.Http.Json;
using Google.Protobuf;
using Microsoft.AspNetCore.Mvc.Testing;

namespace EggIncognito.Tests;

// /api/inspector/decode-response is an egress-free pure-decode helper used by custom-proxy mode: the
// browser already holds the bytes and just needs them rendered. No network, no salt. These boot the
// real host (no AppMode override - the helper is ungated) and prove decode works without any send.
[Collection(SharedAppCollection.Name)]
public class InspectorDecodeResponseTests
{
    private readonly WebApplicationFactory<Program> _factory;

    public InspectorDecodeResponseTests(SharedAppFactory f) => _factory = f;

    [Fact]
    public async Task DecodesKnownResponse()
    {
        var msg = new Ei.PeriodicalsResponse();
        var b64 = System.Convert.ToBase64String(msg.ToByteArray());
        var c = _factory.CreateClient();
        var r = await c.PostAsJsonAsync("/api/inspector/decode-response",
            new { rawBase64 = b64, responseType = "PeriodicalsResponse" });
        Assert.True(r.IsSuccessStatusCode);
        var txt = await r.Content.ReadAsStringAsync();
        Assert.DoesNotContain("\"error\":\"not valid base64", txt);
    }

    [Fact]
    public async Task UnknownType_ReturnsDecodeError_NotCrash()
    {
        var c = _factory.CreateClient();
        var r = await c.PostAsJsonAsync("/api/inspector/decode-response",
            new { rawBase64 = "AA==", responseType = (string?)null });
        Assert.True(r.IsSuccessStatusCode); // decode error is in-band, not an HTTP failure
    }
}
