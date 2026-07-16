using System.Text;
using EggIncognito.Services;
using Google.Protobuf;
using EggIncognito.Capture;

namespace EggIncognito.Tests;


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

    private static string MakeRepo() => TestRepoFixture.MakeRepo(Yaml, "ei-flowproc");

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
        Assert.True(dash.Known);
        Assert.Equal(1, har.Count);
        Assert.True(extractor.Quiet);
    }

    [Fact]
    public void Process_SkippedFlow_FallsBackToNormalizedPathAndEmptyOutcome()
    {
        var root = MakeRepo();
        var extractor = EndpointExtractor.ForRepo(root, null, "EI0000000000000000", false);
        var decoder = new FlowDecoder(root);
        var proc = new FlowProcessor(extractor, decoder, new HarWriter(), root);

       
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
        var dir = (string sub) => Path.Combine(root, "Endpoints", sub);
        var existing = Path.Combine(dir("default"), Slug + ".json");
        var staged = Path.Combine(dir("staged"), Slug + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(existing)!);
        Directory.CreateDirectory(Path.GetDirectoryName(staged)!);
        File.WriteAllText(existing, "a\nb\nb\nc", Encoding.UTF8);
        File.WriteAllText(staged, "a\nb\nd\ne", Encoding.UTF8);

        var (added, removed) = FlowProcessor.DiffCounts(root, Slug);

        Assert.Equal(2, added);
        Assert.Equal(2, removed);
    }

    [Fact]
    public void DiffCounts_MissingFiles_ReturnsZero()
    {
        var root = MakeRepo();
        Assert.Equal((0, 0), FlowProcessor.DiffCounts(root, Slug));
    }
}
