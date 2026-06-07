extern alias Tooling;
using System.Text.Json;
using Cap = Tooling::EggIncognito.Tooling.Capture;
using Dash = Tooling::EggIncognito.Tooling.Dashboard;

namespace EggIncognito.Tests;

// Header capture + redaction for the dashboard. Sensitive header values are redacted in the
// display copy (default view) but preserved in the raw copy (shown only when redaction is Off),
// mirroring the body-redaction model. HarWriter keeps raw headers in the durable artifact.
public class HeaderRedactorTests
{
    [Fact]
    public void Build_RedactsSensitive_KeepsRawCopy()
    {
        var headers = new List<Cap::HttpHeader>
        {
            new("Authorization", "Bearer secret-token"),
            new("Content-Type", "application/x-www-form-urlencoded"),
            new("Cookie", "sid=abc123"),
        };

        var (redacted, raw) = Dash::HeaderRedactor.Build(headers);

        // Redacted copy: secrets masked, others intact.
        Assert.Equal("redacted", redacted[0].Value);
        Assert.True(redacted[0].Sensitive);
        Assert.Equal("application/x-www-form-urlencoded", redacted[1].Value);
        Assert.False(redacted[1].Sensitive);
        Assert.Equal("redacted", redacted[2].Value);

        // Raw copy: original values, but still flagged sensitive so the UI can blur them.
        Assert.Equal("Bearer secret-token", raw[0].Value);
        Assert.True(raw[0].Sensitive);
        Assert.Equal("sid=abc123", raw[2].Value);
    }

    [Fact]
    public void Build_CaseInsensitiveSensitiveMatch()
    {
        var (redacted, _) = Dash::HeaderRedactor.Build([new Cap::HttpHeader("AUTHORIZATION", "x")]);
        Assert.Equal("redacted", redacted[0].Value);
    }

    [Fact]
    public void Build_NullOrEmpty_ReturnsEmpty()
    {
        var (r1, raw1) = Dash::HeaderRedactor.Build(null);
        Assert.Empty(r1);
        Assert.Empty(raw1);
        var (r2, _) = Dash::HeaderRedactor.Build([]);
        Assert.Empty(r2);
    }

    [Fact]
    public void HarWriter_IncludesRawHeaders()
    {
        var har = new Cap::HarWriter();
        har.Add(new Cap::CapturedFlow(
            "https://www.auxbrain.com/ei/x", "POST", 200, "ZGF0YQ==", "cmVzcA==",
            RequestHeaders: [new Cap::HttpHeader("Authorization", "Bearer raw-secret")],
            ResponseHeaders: [new Cap::HttpHeader("Content-Type", "application/octet-stream")]));

        using var doc = JsonDocument.Parse(har.ToHar());
        var entry = doc.RootElement.GetProperty("log").GetProperty("entries")[0];
        var reqHeaders = entry.GetProperty("request").GetProperty("headers");
        var respHeaders = entry.GetProperty("response").GetProperty("headers");

        // HAR keeps the RAW header value (durable artifact; redaction is display-time only).
        Assert.Equal("Authorization", reqHeaders[0].GetProperty("name").GetString());
        Assert.Equal("Bearer raw-secret", reqHeaders[0].GetProperty("value").GetString());
        Assert.Equal("Content-Type", respHeaders[0].GetProperty("name").GetString());
    }
}
