using System.Net;
using System.Text.Json;
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
    private const string NeedsDb = "needs a reachable Postgres";

    private readonly WebApplicationFactory<Program> _factory = f;

    private static string Url(string from, string to, string? query = null) =>
        $"{Base}?platform={Platform}&from={from}&to={to}" + (query is null ? "" : "&" + query);

    [GeneratedRegex("\"traceId\":\"[^\"]*\"")]
    private static partial Regex TraceIdField();

    private static async Task<string> StableBodyAsync(HttpResponseMessage res) =>
        TraceIdField().Replace(await res.Content.ReadAsStringAsync(), "\"traceId\":\"\"");

    private static IEnumerable<string> Attachment(HttpResponseMessage res) {
        if (res.Content.Headers.TryGetValues("Content-Disposition", out var values)) return values;
        return res.Headers.TryGetValues("Content-Disposition", out var fallback) ? fallback : [];
    }

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

    [Fact(Skip = NeedsDb)]
    public async Task FormatAbsent_MatchesFormatText() {
        using var c = _factory.CreateClient();
        var bare = await c.GetAsync(Url(From, To));
        var text = await c.GetAsync(Url(From, To, "format=text"));
        bare.EnsureSuccessStatusCode();
        Assert.Equal(bare.StatusCode, text.StatusCode);
        Assert.Equal("text/plain", bare.Content.Headers.ContentType?.MediaType);
        Assert.Equal(await bare.Content.ReadAsStringAsync(), await text.Content.ReadAsStringAsync());
    }

    [Fact(Skip = NeedsDb)]
    public async Task Unified_IsAPatch() {
        using var c = _factory.CreateClient();
        var res = await c.GetAsync(Url(From, To, "format=unified"));
        res.EnsureSuccessStatusCode();
        Assert.Equal("text/plain", res.Content.Headers.ContentType?.MediaType);
        Assert.StartsWith("--- ", await res.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact(Skip = NeedsDb)]
    public async Task Unified_WithZeroContext_HasNoContextLines() {
        using var c = _factory.CreateClient();
        var res = await c.GetAsync(Url(From, To, "format=unified&context=0"));
        res.EnsureSuccessStatusCode();
        string body = await res.Content.ReadAsStringAsync();
        Assert.DoesNotContain(body.Split('\n'), l => l.StartsWith(' '));
    }

    [Fact(Skip = NeedsDb)]
    public async Task Unified_WithDownload_SetsContentDisposition() {
        using var c = _factory.CreateClient();
        var res = await c.GetAsync(Url(From, To, "format=unified&download=1"));
        res.EnsureSuccessStatusCode();
        Assert.True(res.Content.Headers.TryGetValues("Content-Disposition", out var values)
                    || res.Headers.TryGetValues("Content-Disposition", out values));
        Assert.Contains(values!, v => v.Contains(".diff", StringComparison.Ordinal));
    }

    [Fact(Skip = NeedsDb)]
    public async Task Text_WithDownload_SetsTextContentDisposition() {
        using var c = _factory.CreateClient();
        var res = await c.GetAsync(Url(From, To, "format=text&download=1"));
        res.EnsureSuccessStatusCode();
        Assert.Contains(Attachment(res), v => v.Contains(".txt", StringComparison.Ordinal));
    }

    [Fact(Skip = NeedsDb)]
    public async Task FormatAbsent_WithDownload_SetsTextContentDisposition() {
        using var c = _factory.CreateClient();
        var res = await c.GetAsync(Url(From, To, "download=1"));
        res.EnsureSuccessStatusCode();
        Assert.Contains(Attachment(res), v => v.Contains(".txt", StringComparison.Ordinal));
    }

    [Fact(Skip = NeedsDb)]
    public async Task FormatAbsent_WithoutDownload_HasNoContentDisposition() {
        using var c = _factory.CreateClient();
        var res = await c.GetAsync(Url(From, To));
        res.EnsureSuccessStatusCode();
        Assert.Empty(Attachment(res));
    }

    [Fact(Skip = NeedsDb)]
    public async Task Split_WithDownload_SetsJsonContentDisposition() {
        using var c = _factory.CreateClient();
        var res = await c.GetAsync(Url(From, To, "format=split&download=1"));
        res.EnsureSuccessStatusCode();
        Assert.Contains(Attachment(res), v => v.Contains(".json", StringComparison.Ordinal));
    }

    [Fact(Skip = NeedsDb)]
    public async Task Json_WithDownload_SetsJsonContentDisposition() {
        using var c = _factory.CreateClient();
        var res = await c.GetAsync(Url(From, To, "format=json&download=1"));
        res.EnsureSuccessStatusCode();
        Assert.Contains(Attachment(res), v => v.Contains(".json", StringComparison.Ordinal));
    }

    [Fact(Skip = NeedsDb)]
    public async Task Json_CarriesEntriesAndSummary() {
        using var c = _factory.CreateClient();
        var res = await c.GetAsync(Url(From, To, "format=json"));
        res.EnsureSuccessStatusCode();
        Assert.Equal("application/json", res.Content.Headers.ContentType?.MediaType);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.TryGetProperty("entries", out _));
        Assert.True(doc.RootElement.TryGetProperty("summary", out _));
    }

    [Fact(Skip = NeedsDb)]
    public async Task Split_CarriesRowsAndHunkStarts() {
        using var c = _factory.CreateClient();
        var res = await c.GetAsync(Url(From, To, "format=split"));
        res.EnsureSuccessStatusCode();
        Assert.Equal("application/json", res.Content.Headers.ContentType?.MediaType);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.TryGetProperty("rows", out _));
        Assert.True(doc.RootElement.TryGetProperty("hunkStarts", out _));
    }
}
