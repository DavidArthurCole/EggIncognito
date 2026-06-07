using EggIncognito.Services;
using Google.Protobuf;
using EggIncognito.Capture;

namespace EggIncognito.Tests;

// Proves the capture path is self-consistent: a flow written to a HAR by HarWriter and then
// re-read by EndpointExtractor.RunFromHar produces the SAME endpoint as feeding that flow straight
// to ProcessFlow. This is the "re-running the HAR is a no-op" guarantee from the plan, and it is
// what lets the durable HAR be the hand-off artifact for the in-process capture.
public class HarWriterRoundTripTests
{
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

    private static string MakeRepo()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ei-har-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "EggIncognito", "RouteMap"));
        File.WriteAllText(Path.Combine(root, "EggIncognito.slnx"), "<Solution />");
        File.WriteAllText(Path.Combine(root, "EggIncognito", "RouteMap", "routes.yaml"), Yaml);
        return root;
    }

    private static string ResponseB64()
    {
        var inner = new Ei.PeriodicalsResponse();
        var outer = new Ei.AuthenticatedMessage { Message = inner.ToByteString(), Compressed = false };
        return Convert.ToBase64String(outer.ToByteArray());
    }

    private static string EndpointPath(string root) =>
        Path.Combine(root, "EggIncognito", "Endpoints", "default", Slug + ".json");

    [Fact]
    public void HarWriter_Output_FedBackThroughExtractor_MatchesDirectFlow()
    {
        var flow = new CapturedFlow(Url, "POST", 200, RequestDataB64: null, ResponseBodyB64: ResponseB64());

        // Direct: feed the flow straight to ProcessFlow.
        var directRoot = MakeRepo();
        var direct = EndpointExtractor.ForRepo(directRoot, null, "EI0000000000000000", false);
        direct.ProcessFlow(flow.Url, flow.Method, flow.Status, flow.RequestDataB64, flow.ResponseBodyB64);
        direct.Save();
        var directEndpoint = File.ReadAllText(EndpointPath(directRoot));

        // Via HAR: HarWriter emits the flow, RunFromHar reads it back.
        var harRoot = MakeRepo();
        var writer = new HarWriter();
        writer.Add(flow);
        Assert.Equal(1, writer.Count);
        var harFile = Path.Combine(harRoot, "session.har");
        writer.Save(harFile);

        var viaHar = EndpointExtractor.ForRepo(harRoot, null, "EI0000000000000000", false);
        viaHar.RunFromHar(harFile);
        viaHar.Save();
        var harEndpoint = File.ReadAllText(EndpointPath(harRoot));

        Assert.Equal(directEndpoint, harEndpoint);
        Assert.Equal(direct.Counts.Wrote, viaHar.Counts.Wrote);
    }

    [Fact]
    public void HarWriter_EmitsRequestDataParam_WhenPresent()
    {
        var flow = new CapturedFlow(Url, "POST", 200, RequestDataB64: "AAEC", ResponseBodyB64: ResponseB64());
        var writer = new HarWriter();
        writer.Add(flow);
        var har = writer.ToHar();
        // The data param must be present so EndpointExtractor.ReadRequestData can recover it.
        Assert.Contains("\"data\"", har);
        Assert.Contains("AAEC", har);
    }
}
