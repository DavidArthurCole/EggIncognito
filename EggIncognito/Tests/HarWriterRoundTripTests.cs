using System.Text.Json;
using EggIncognito.Capture;
using EggIncognito.Services;
using Ei;
using Google.Protobuf;

namespace EggIncognito.Tests;

public sealed class HarWriterRoundTripTests : IDisposable {
    private readonly TempDir _tmp = new();

    public void Dispose() => _tmp.Dispose();

    private const string Url = "https://www.auxbrain.com/ei/get_periodicals";
    private const string Slug = "ei/get_periodicals";

    private const string Yaml = """
                                routes:
                                  # ei/
                                  - path: ei/get_periodicals
                                    request: GetPeriodicalsRequest
                                    response: PeriodicalsResponse

                                needs_capture:
                                  request_unknown:
                                """;

    private string MakeRepo() => TestRepoFixture.MakeRepo(_tmp, Yaml);

    private static string ResponseB64() {
        var inner = new PeriodicalsResponse();
        var outer = new AuthenticatedMessage { Message = inner.ToByteString(), Compressed = false };
        return Convert.ToBase64String(outer.ToByteArray());
    }

    private static string EndpointPath(string root) =>
        Path.Combine(root, "Endpoints", "default", Slug + ".json");

    [Fact]
    public void HarWriter_Output_FedBackThroughExtractor_MatchesDirectFlow() {
        var flow = new CapturedFlow(Url, "POST", 200, null, ResponseB64());


        string directRoot = MakeRepo();
        var direct = EndpointExtractor.ForRepo(directRoot, null, "EI0000000000000000", false);
        direct.ProcessFlow(flow.Url, flow.Method, flow.Status, flow.RequestDataB64, flow.ResponseBodyB64);
        direct.Save();
        string directEndpoint = File.ReadAllText(EndpointPath(directRoot));


        string harRoot = MakeRepo();
        var writer = new HarWriter();
        writer.Add(flow);
        Assert.Equal(1, writer.Count);
        string harFile = Path.Combine(harRoot, "session.har");
        writer.Save(harFile);

        var viaHar = EndpointExtractor.ForRepo(harRoot, null, "EI0000000000000000", false);
        viaHar.RunFromHar(harFile);
        viaHar.Save();
        string harEndpoint = File.ReadAllText(EndpointPath(harRoot));

        Assert.Equal(directEndpoint, harEndpoint);
        Assert.Equal(direct.Counts.Wrote, viaHar.Counts.Wrote);
    }

    [Fact]
    public void HarWriter_EmitsRequestDataParam_WhenPresent() {
        var flow = new CapturedFlow(Url, "POST", 200, "AAEC", ResponseB64());
        var writer = new HarWriter();
        writer.Add(flow);
        string har = writer.ToHar();

        Assert.Contains("\"data\"", har);
        Assert.Contains("AAEC", har);
    }


    [Fact]
    public async Task HarWriter_ConcurrentAddAndToHar_NeverThrows_AndSnapshotsParse() {
        var flow = new CapturedFlow(Url, "POST", 200, "AAEC", ResponseB64());
        var writer = new HarWriter();
        const int total = 2000;
        using var start = new ManualResetEventSlim(false);

        var adder = Task.Run(() => {
            start.Wait();
            for (int i = 0; i < total; i++) writer.Add(flow);
        });
        var serializer = Task.Run(() => {
            start.Wait();
            while (writer.Count < total) {
                using var doc = JsonDocument.Parse(writer.ToHar());
            }
        });
        start.Set();
        await Task.WhenAll(adder, serializer);

        using var final = JsonDocument.Parse(writer.ToHar());
        var entries = final.RootElement.GetProperty("log").GetProperty("entries");
        Assert.Equal(total, entries.GetArrayLength());
    }
}
