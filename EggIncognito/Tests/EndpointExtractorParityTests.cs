using System.Text;
using System.Text.Json;
using EggIncognito.Core.Services;
using Ei;
using Google.Protobuf;

namespace EggIncognito.Tests;

public sealed class EndpointExtractorParityTests : IDisposable {
    private readonly TempDir _tmp = new();

    public void Dispose() => _tmp.Dispose();

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

    private string MakeRepo() => TestRepoFixture.MakeRepo(_tmp, Yaml);

    private static string WrappedResponseB64() {
        var inner = new PeriodicalsResponse();
        var outer = new AuthenticatedMessage { Message = inner.ToByteString(), Compressed = false };
        return Convert.ToBase64String(outer.ToByteArray());
    }

    private static string EndpointPath(string root) =>
        Path.Combine(root, "Endpoints", "default", Slug + ".json");

    [Fact]
    public void ProcessFlow_MatchesHarPath_ByteForByte() {
        string responseB64 = WrappedResponseB64();

        string inProcRoot = MakeRepo();
        var inProc = EndpointExtractor.ForRepo(inProcRoot, null, "EI0000000000000000", false);
        string? path = inProc.ProcessFlow(Url, "POST", 200, null, responseB64);
        inProc.Save();

        Assert.Equal(Slug, path);
        Assert.True(File.Exists(EndpointPath(inProcRoot)));
        string inProcEndpoint = File.ReadAllText(EndpointPath(inProcRoot));

        string harRoot = MakeRepo();
        string harFile = Path.Combine(harRoot, "session.har");
        File.WriteAllText(harFile, BuildHar(Url, responseB64), new UTF8Encoding(false));

        var harExtractor = EndpointExtractor.ForRepo(harRoot, null, "EI0000000000000000", false);
        harExtractor.RunFromHar(harFile);
        harExtractor.Save();

        Assert.True(File.Exists(EndpointPath(harRoot)));
        string harEndpoint = File.ReadAllText(EndpointPath(harRoot));

        Assert.Equal(harEndpoint, inProcEndpoint);
        Assert.Equal(1, inProc.Counts.Wrote);
        Assert.Equal(inProc.Counts.Wrote, harExtractor.Counts.Wrote);
    }

    [Fact]
    public void ProcessFlow_SkipsNonPostAndNon200() {
        string responseB64 = WrappedResponseB64();
        string root = MakeRepo();
        var ex = EndpointExtractor.ForRepo(root, null, "EI0000000000000000", false);

        Assert.Null(ex.ProcessFlow(Url, "GET", 200, null, responseB64));
        Assert.Null(ex.ProcessFlow(Url, "POST", 404, null, responseB64));
        Assert.False(File.Exists(EndpointPath(root)));
    }

    [Fact]
    public void ProcessFlow_DedupesRepeatedFlow() {
        string responseB64 = WrappedResponseB64();
        string root = MakeRepo();
        var ex = EndpointExtractor.ForRepo(root, null, "EI0000000000000000", false);

        Assert.Equal(Slug, ex.ProcessFlow(Url, "POST", 200, null, responseB64));
        Assert.Null(ex.ProcessFlow(Url, "POST", 200, null, responseB64));
        Assert.Equal(1, ex.Counts.Wrote);
    }

    [Fact]
    public void ForceWriteEndpoint_BypassesDedupAndOverwrites() {
        string responseB64 = WrappedResponseB64();
        string root = MakeRepo();
        var ex = EndpointExtractor.ForRepo(root, null, "EI0000000000000000", false);

        Assert.Equal(Slug, ex.ProcessFlow(Url, "POST", 200, null, responseB64));
        Assert.Null(ex.ProcessFlow(Url, "POST", 200, null, responseB64));

        Assert.Equal(Slug, ex.ForceWriteEndpoint(Url, "POST", 200, null, responseB64));
        Assert.True(File.Exists(EndpointPath(root)));
    }

    [Fact]
    public void ForceWriteEndpoint_RegistersUnmappedRequestType() {
        string root = _tmp.CreateSubdir();
        Directory.CreateDirectory(Path.Combine(root, "RouteMap"));
        File.WriteAllText(Path.Combine(root, "EggIncognito.slnx"), "<Solution />");
        File.WriteAllText(Path.Combine(root, "RouteMap", "routes.yaml"), """
                                                                         routes:
                                                                           - path: ei/get_periodicals
                                                                             request:  # TODO review - request type not detected
                                                                             response: PeriodicalsResponse
                                                                         """);
        var ex = EndpointExtractor.ForRepo(root, null, "EI0000000000000000", false);

        var reqMsg = new GetPeriodicalsRequest {
            UserId = "EI123",
            CurrentClientVersion = 72,
            Debug = false,
            SoulEggs = 1_000_000_000.0,
            PiggyFull = true,
            PiggyFoundFull = true,
            SecondsFullRealtime = 25_000_000,
            SecondsFullGametime = 400_000
        };
        string reqB64 = Convert.ToBase64String(reqMsg.ToByteArray());
        ex.ForceWriteEndpoint(Url, "POST", 200, reqB64, WrappedResponseB64());
        ex.Save();

        string yaml = File.ReadAllText(Path.Combine(root, "RouteMap", "routes.yaml"));
        Assert.Contains("request: GetPeriodicalsRequest", yaml);
        Assert.DoesNotContain("TODO", yaml);
    }

    [Fact]
    public void ForceWriteEndpoint_RejectsNonPostOrNon200() {
        string responseB64 = WrappedResponseB64();
        string root = MakeRepo();
        var ex = EndpointExtractor.ForRepo(root, null, "EI0000000000000000", false);

        Assert.Null(ex.ForceWriteEndpoint(Url, "GET", 200, null, responseB64));
        Assert.Null(ex.ForceWriteEndpoint(Url, "POST", 500, null, responseB64));
    }

    private static string BuildHar(string url, string responseBodyB64) {
        var har = new {
            log = new {
                version = "1.2",
                entries = new[] {
                    new {
                        request = new {
                            method = "POST",
                            url,
                            postData = new
                                { mimeType = "application/x-www-form-urlencoded", @params = Array.Empty<object>() }
                        },
                        response = new {
                            status = 200,
                            content = new { text = responseBodyB64 }
                        }
                    }
                }
            }
        };
        return JsonSerializer.Serialize(har);
    }
}
