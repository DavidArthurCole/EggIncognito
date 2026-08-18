using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace EggIncognito.Tests;

[Collection(SharedAppCollection.Name)]
public partial class ProtoDiffFormatTests(SharedAppFactory f) {
    private const string Platform = "ios";
    private const string From = "1.36.0.2";
    private const string To = "1.37.0.1";
    private const string Base = "/api/protos/diff";
    private const string Missing = "does-not-exist-9.9.9";

    private readonly WebApplicationFactory<Program> _factory = f;

    private static string Url(string from, string to, string? query = null) =>
        $"{Base}?platform={Platform}&from={from}&to={to}" + (query is null ? "" : "&" + query);

    [GeneratedRegex("\"traceId\":\"[^\"]*\"")]
    private static partial Regex TraceIdField();

    private static async Task<string> StableBodyAsync(HttpResponseMessage res) =>
        TraceIdField().Replace(await res.Content.ReadAsStringAsync(), "\"traceId\":\"\"");

    [Fact]
    public async Task MissingParameters_Is400() {
        using var c = _factory.CreateClient();
        var res = await c.GetAsync(Base);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task UnknownFormat_Is400() {
        using var c = _factory.CreateClient();
        var res = await c.GetAsync(Url(From, To, "format=bogus"));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Theory]
    [InlineData("text")]
    [InlineData("unified")]
    [InlineData("json")]
    [InlineData("split")]
    [InlineData("UNIFIED")]
    public async Task KnownFormats_AreNotRejected(string format) {
        using var c = _factory.CreateClient();
        var res = await c.GetAsync(Url(Missing, Missing, "format=" + format));
        Assert.NotEqual(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task UnknownVersion_Is404() {
        using var c = _factory.CreateClient();
        var res = await c.GetAsync(Url(Missing, Missing));
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task UnknownVersion_WithFormat_StaysConsistentWithDefault() {
        using var c = _factory.CreateClient();
        var bare = await c.GetAsync(Url(Missing, Missing));
        var text = await c.GetAsync(Url(Missing, Missing, "format=text"));
        Assert.Equal(bare.StatusCode, text.StatusCode);
        Assert.Equal(await StableBodyAsync(bare), await StableBodyAsync(text));
    }
}
