using System.Text;
using EggIncognito.Services;
using Google.Protobuf;
using EggIncognito.Capture;

namespace EggIncognito.Tests;

// FlowProcessor is the per-flow core of the capture command, lifted out of the inline RunAsync
// lambda so it can be tested. These prove a synthetic CapturedFlow yields the expected
// DashboardFlow (outcome, types, known) and exercise the pure helpers (OutcomeDelta, DiffCounts).
public class FlowProcessorTests
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
        var root = Path.Combine(Path.GetTempPath(), $"ei-flowproc-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "EggIncognito", "RouteMap"));
        File.WriteAllText(Path.Combine(root, "EggIncognito.slnx"), "<Solution />");
        File.WriteAllText(Path.Combine(root, "EggIncognito", "RouteMap", "routes.yaml"), Yaml);
        return root;
    }

    private static string WrappedResponseB64()
    {
        var inner = new Ei.PeriodicalsResponse();
        var outer = new Ei.AuthenticatedMessage { Message = inner.ToByteString(), Compressed = false };
        return Convert.ToBase64String(outer.ToByteArray());
    }

    [Fact]
    public void Process_NewKnownEndpoint_YieldsWroteOutcomeAndKnownFlow()
    {
        var root = MakeRepo();
        var extractor = EndpointExtractor.ForRepo(root, eid: null, eidPlaceholder: "EI0000000000000000", overwrite: false);
        var decoder = new FlowDecoder(root);
        var har = new HarWriter();
        var proc = new FlowProcessor(extractor, decoder, har, root);

        var dash = proc.Process(new CapturedFlow(Url, "POST", 200, null, WrappedResponseB64()));

        Assert.Equal(Slug, dash.Path);
        Assert.Equal("wrote", dash.Outcome);
        Assert.Equal("PeriodicalsResponse", dash.ResponseType);
        Assert.True(dash.Known); // response is yaml-mapped; request has no body
        Assert.Equal(1, har.Count); // HAR entry appended
        Assert.True(extractor.Quiet); // FlowProcessor put the extractor in quiet mode
    }

    [Fact]
    public void Process_SkippedFlow_FallsBackToNormalizedPathAndEmptyOutcome()
    {
        var root = MakeRepo();
        var extractor = EndpointExtractor.ForRepo(root, null, "EI0000000000000000", false);
        var decoder = new FlowDecoder(root);
        var proc = new FlowProcessor(extractor, decoder, new HarWriter(), root);

        // 404 -> ProcessFlow returns null (skipped). Display path falls back to the normalized URL.
        var dash = proc.Process(new CapturedFlow(Url, "POST", 404, null, WrappedResponseB64()));

        Assert.Equal(Slug, dash.Path);
        Assert.Equal("", dash.Outcome);
    }

    [Fact]
    public void OutcomeDelta_ReportsTheSingleChangedTally()
    {
        var a = (wrote: 0, upd: 0, diff: 0, same: 0, loss: 0);
        Assert.Equal("wrote", FlowProcessor.OutcomeDelta(a, (1, 0, 0, 0, 0)));
        Assert.Equal("upd", FlowProcessor.OutcomeDelta(a, (0, 1, 0, 0, 0)));
        Assert.Equal("diff", FlowProcessor.OutcomeDelta(a, (0, 0, 1, 0, 0)));
        Assert.Equal("loss", FlowProcessor.OutcomeDelta(a, (0, 0, 0, 0, 1)));
        Assert.Equal("same", FlowProcessor.OutcomeDelta(a, (0, 0, 0, 1, 0)));
        Assert.Equal("", FlowProcessor.OutcomeDelta(a, a));
    }

    [Fact]
    public void DiffCounts_CountsAddedAndRemovedLines_Multiset()
    {
        var root = MakeRepo();
        var dir = (string sub) => Path.Combine(root, "EggIncognito", "Endpoints", sub);
        var existing = Path.Combine(dir("default"), Slug + ".json");
        var staged = Path.Combine(dir("staged"), Slug + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(existing)!);
        Directory.CreateDirectory(Path.GetDirectoryName(staged)!);
        File.WriteAllText(existing, "a\nb\nb\nc", Encoding.UTF8); // removes one 'b' and 'c'
        File.WriteAllText(staged, "a\nb\nd\ne", Encoding.UTF8); // adds 'd' and 'e'

        var (added, removed) = FlowProcessor.DiffCounts(root, Slug);

        Assert.Equal(2, added); // d, e
        Assert.Equal(2, removed); // one b, c
    }

    [Fact]
    public void DiffCounts_MissingFiles_ReturnsZero()
    {
        var root = MakeRepo();
        Assert.Equal((0, 0), FlowProcessor.DiffCounts(root, Slug));
    }
}
