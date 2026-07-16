using System.Text;
using System.Text.Json;
using EggIncognito.Services;
using Google.Protobuf;

namespace EggIncognito.Tests;

public class EndpointExtractorParityTests
{
    private const string Url = "https://www.auxbrain.com/ei/get_periodicals";
    private const string Slug = "ei/get_periodicals";

    private const string Yaml = """
routes:
  - path: ei/get_periodicals
    request: GetPeriodicalsRequest
    response: PeriodicalsResponse

needs_capture:
  request_unknown:
""";

    private static string MakeRepo() => TestRepoFixture.MakeRepo(Yaml, "ei-extract");

    private static string WrappedResponseB64()
    {
        var inner = new Ei.PeriodicalsResponse();
        var outer = new Ei.AuthenticatedMessage { Message = inner.ToByteString(), Compressed = false };
        return Convert.ToBase64String(outer.ToByteArray());
    }

    private static string EndpointPath(string root) =>
        Path.Combine(root, "Endpoints", "default", Slug + ".json");

    [Fact]
    public void ProcessFlow_MatchesHarPath_ByteForByte()
    {
        var responseB64 = WrappedResponseB64();

        var inProcRoot = MakeRepo();
        var inProc = EndpointExtractor.ForRepo(inProcRoot, eid: null, eidPlaceholder: "EI0000000000000000", overwrite: false);
        var path = inProc.ProcessFlow(Url, "POST", 200, requestDataB64: null, responseBodyB64: responseB64);
        inProc.Save();

        Assert.Equal(Slug, path);
        Assert.True(File.Exists(EndpointPath(inProcRoot)));
        var inProcEndpoint = File.ReadAllText(EndpointPath(inProcRoot));

        var harRoot = MakeRepo();
        var harFile = Path.Combine(harRoot, "session.har");
        File.WriteAllText(harFile, BuildHar(Url, responseB64), new UTF8Encoding(false));

        var harExtractor = EndpointExtractor.ForRepo(harRoot, eid: null, eidPlaceholder: "EI0000000000000000", overwrite: false);
        harExtractor.RunFromHar(harFile);
        harExtractor.Save();

        Assert.True(File.Exists(EndpointPath(harRoot)));
        var harEndpoint = File.ReadAllText(EndpointPath(harRoot));

        Assert.Equal(harEndpoint, inProcEndpoint);
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
        Assert.False(File.Exists(EndpointPath(root)));
    }

    [Fact]
    public void ProcessFlow_DedupesRepeatedFlow()
    {
        var responseB64 = WrappedResponseB64();
        var root = MakeRepo();
        var ex = EndpointExtractor.ForRepo(root, null, "EI0000000000000000", false);

        Assert.Equal(Slug, ex.ProcessFlow(Url, "POST", 200, null, responseB64));
        Assert.Null(ex.ProcessFlow(Url, "POST", 200, null, responseB64));
        Assert.Equal(1, ex.Counts.Wrote);
    }

    [Fact]
    public void ForceWriteEndpoint_BypassesDedupAndOverwrites()
    {
        var responseB64 = WrappedResponseB64();
        var root = MakeRepo();
        var ex = EndpointExtractor.ForRepo(root, null, "EI0000000000000000", overwrite: false);

        Assert.Equal(Slug, ex.ProcessFlow(Url, "POST", 200, null, responseB64));
        Assert.Null(ex.ProcessFlow(Url, "POST", 200, null, responseB64));

        Assert.Equal(Slug, ex.ForceWriteEndpoint(Url, "POST", 200, null, responseB64));
        Assert.True(File.Exists(EndpointPath(root)));
    }

    [Fact]
    public void ForceWriteEndpoint_RegistersUnmappedRequestType()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ei-reg-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "RouteMap"));
        File.WriteAllText(Path.Combine(root, "EggIncognito.slnx"), "<Solution />");
        File.WriteAllText(Path.Combine(root, "RouteMap", "routes.yaml"), """
routes:
  - path: ei/get_periodicals
    request:  # TODO review - request type not detected
    response: PeriodicalsResponse
""");
        var ex = EndpointExtractor.ForRepo(root, null, "EI0000000000000000", overwrite: false);

        var reqMsg = new Ei.GetPeriodicalsRequest
        {
            UserId = "EI123", CurrentClientVersion = 72, Debug = false,
            SoulEggs = 1_000_000_000.0, PiggyFull = true, PiggyFoundFull = true,
            SecondsFullRealtime = 25_000_000, SecondsFullGametime = 400_000,
        };
        var reqB64 = Convert.ToBase64String(reqMsg.ToByteArray());
        ex.ForceWriteEndpoint(Url, "POST", 200, reqB64, WrappedResponseB64());
        ex.Save();

        var yaml = File.ReadAllText(Path.Combine(root, "RouteMap", "routes.yaml"));
        Assert.Contains("request: GetPeriodicalsRequest", yaml);
        Assert.DoesNotContain("TODO", yaml);
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
