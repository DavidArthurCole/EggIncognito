using System.Text;
using System.Text.Json;
using EggIncognito.Services;
using Google.Protobuf;

namespace EggIncognito.Tests;

// Proves the in-process per-flow path (ProcessFlow) and the HAR-file path (RunFromHar) produce
// byte-identical fixtures + identical yaml/self-repair effects for the same flow. This is the
// guard that the capture proxy (which feeds ProcessFlow) cannot diverge from the Seeder's
// established --from-har behavior.
public class EndpointExtractorParityTests
{
    // A real type-mapped endpoint: ei/get_periodicals -> PeriodicalsResponse (known response).
    private const string Url = "https://www.auxbrain.com/ei/get_periodicals";
    private const string Slug = "ei/get_periodicals";

    // Minimal routes.yaml carrying just the endpoint under test, in canonical form.
    private const string Yaml = """
routes:
  # ei/
  - path: ei/get_periodicals
    request: GetPeriodicalsRequest
    response: PeriodicalsResponse

needs_capture:
  request_unknown:
""";

    private static string MakeRepo()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ei-extract-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "EggIncognito", "RouteMap"));
        // A .slnx marker so any repo-root walk lands here.
        File.WriteAllText(Path.Combine(root, "EggIncognito.slnx"), "<Solution />");
        File.WriteAllText(Path.Combine(root, "EggIncognito", "RouteMap", "routes.yaml"), Yaml);
        return root;
    }

    // Base64 of an AuthenticatedMessage wrapping an inner PeriodicalsResponse - exactly the wire
    // framing the real API returns and both extraction paths decode.
    private static string WrappedResponseB64()
    {
        var inner = new Ei.PeriodicalsResponse();
        var outer = new Ei.AuthenticatedMessage { Message = inner.ToByteString(), Compressed = false };
        return Convert.ToBase64String(outer.ToByteArray());
    }

    private static string FixturePath(string root) =>
        Path.Combine(root, "EggIncognito", "Endpoints", "default", Slug + ".json");

    [Fact]
    public void ProcessFlow_MatchesHarPath_ByteForByte()
    {
        var responseB64 = WrappedResponseB64();

        // --- in-process path ---
        var inProcRoot = MakeRepo();
        var inProc = EndpointExtractor.ForRepo(inProcRoot, eid: null, eidPlaceholder: "EI0000000000000000", overwrite: false);
        var path = inProc.ProcessFlow(Url, "POST", 200, requestDataB64: null, responseBodyB64: responseB64);
        inProc.Save();

        Assert.Equal(Slug, path);
        Assert.True(File.Exists(FixturePath(inProcRoot)));
        var inProcFixture = File.ReadAllText(FixturePath(inProcRoot));

        // --- HAR-file path: synthesize a HAR carrying the identical flow ---
        var harRoot = MakeRepo();
        var harFile = Path.Combine(harRoot, "session.har");
        File.WriteAllText(harFile, BuildHar(Url, responseB64), new UTF8Encoding(false));

        var harExtractor = EndpointExtractor.ForRepo(harRoot, eid: null, eidPlaceholder: "EI0000000000000000", overwrite: false);
        harExtractor.RunFromHar(harFile);
        harExtractor.Save();

        Assert.True(File.Exists(FixturePath(harRoot)));
        var harFixture = File.ReadAllText(FixturePath(harRoot));

        // Byte-for-byte fixture parity + identical write tally.
        Assert.Equal(harFixture, inProcFixture);
        Assert.Equal(1, inProc.Counts.Wrote);
        Assert.Equal(inProc.Counts.Wrote, harExtractor.Counts.Wrote);
    }

    [Fact]
    public void ProcessFlow_SkipsNonPostAndNon200()
    {
        var responseB64 = WrappedResponseB64();
        var root = MakeRepo();
        var ex = EndpointExtractor.ForRepo(root, null, "EI0000000000000000", false);

        Assert.Null(ex.ProcessFlow(Url, "GET", 200, null, responseB64));
        Assert.Null(ex.ProcessFlow(Url, "POST", 404, null, responseB64));
        Assert.False(File.Exists(FixturePath(root)));
    }

    [Fact]
    public void ProcessFlow_DedupesRepeatedFlow()
    {
        var responseB64 = WrappedResponseB64();
        var root = MakeRepo();
        var ex = EndpointExtractor.ForRepo(root, null, "EI0000000000000000", false);

        Assert.Equal(Slug, ex.ProcessFlow(Url, "POST", 200, null, responseB64));
        Assert.Null(ex.ProcessFlow(Url, "POST", 200, null, responseB64)); // second time deduped
        Assert.Equal(1, ex.Counts.Wrote);
    }

    // The dashboard "Save as fixture" button: the live capture already processed (and deduped) the
    // flow, so ProcessFlow would skip it. ForceWriteEndpoint must bypass dedup AND force-overwrite.
    [Fact]
    public void ForceWriteEndpoint_BypassesDedupAndOverwrites()
    {
        var responseB64 = WrappedResponseB64();
        var root = MakeRepo();
        // overwrite:false (the live default) - the force path must override this.
        var ex = EndpointExtractor.ForRepo(root, null, "EI0000000000000000", overwrite: false);

        // Live capture processed it once (added to dedup set).
        Assert.Equal(Slug, ex.ProcessFlow(Url, "POST", 200, null, responseB64));
        // ProcessFlow now skips it (deduped) - this is the bug the button hit.
        Assert.Null(ex.ProcessFlow(Url, "POST", 200, null, responseB64));

        // ForceWriteEndpoint writes it anyway.
        Assert.Equal(Slug, ex.ForceWriteEndpoint(Url, "POST", 200, null, responseB64));
        Assert.True(File.Exists(FixturePath(root)));
    }

    [Fact]
    public void ForceWriteEndpoint_RejectsNonPostOrNon200()
    {
        var responseB64 = WrappedResponseB64();
        var root = MakeRepo();
        var ex = EndpointExtractor.ForRepo(root, null, "EI0000000000000000", false);

        Assert.Null(ex.ForceWriteEndpoint(Url, "GET", 200, null, responseB64));
        Assert.Null(ex.ForceWriteEndpoint(Url, "POST", 500, null, responseB64));
    }

    // Build a one-entry HAR whose request/response mirror the in-process flow inputs.
    private static string BuildHar(string url, string responseBodyB64)
    {
        var har = new
        {
            log = new
            {
                version = "1.2",
                entries = new[]
                {
                    new
                    {
                        request = new
                        {
                            method = "POST",
                            url,
                            postData = new { mimeType = "application/x-www-form-urlencoded", @params = Array.Empty<object>() },
                        },
                        response = new
                        {
                            status = 200,
                            content = new { text = responseBodyB64 },
                        },
                    },
                },
            },
        };
        return JsonSerializer.Serialize(har);
    }
}
