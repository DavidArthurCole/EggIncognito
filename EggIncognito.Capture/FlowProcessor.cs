using EggIncognito.Services;

namespace EggIncognito.Capture;

// The core per-flow work of capture, separated from the CaptureSession queue consumer so it is
// unit-testable and needs no console-suppression hack; the extractor is put in Quiet mode instead.
// For each captured flow it:
//   - appends a HAR entry, the durable hand-off artifact,
//   - runs the endpoint extractor: decode, redact, self-repair routes.yaml, write/stage,
//   - derives the per-flow outcome from the extractor's Counts delta,
//   - decodes display JSON, raw + redacted with proto type names, via FlowDecoder,
//   - computes git-style diff +/- counts for a "diff" outcome,
// and returns the DashboardFlow to publish. Id/Timestamp are owned by the hub at publish time.
public sealed class FlowProcessor
{
    private readonly EndpointExtractor _extractor;
    private readonly FlowDecoder _decoder;
    private readonly HarWriter _har;
    private readonly string _contentRoot;

    public FlowProcessor(EndpointExtractor extractor, FlowDecoder decoder, HarWriter har, string contentRoot)
    {
        _extractor = extractor;
        // The extractor's per-flow console chatter belongs in the dashboard, not stdout.
        _extractor.Quiet = true;
        _decoder = decoder;
        _har = har;
        _contentRoot = contentRoot;
    }

    public DashboardFlow Process(CapturedFlow flow)
    {
        _har.Add(flow);

        var before = Snapshot(_extractor.Counts);
        var path = _extractor.ProcessFlow(flow.Url, flow.Method, flow.Status, flow.RequestDataB64, flow.ResponseBodyB64);
        var outcome = OutcomeDelta(before, Snapshot(_extractor.Counts));

        var displayPath = path ?? EndpointExtractor.NormalizePath(flow.Url);
        var req = _decoder.DecodeRequest(displayPath, flow.RequestDataB64);
        var resp = _decoder.DecodeResponse(displayPath, flow.ResponseBodyB64);
        // "Known" = both sides resolve to yaml-mapped types, or the request has no body: an endpoint we
        // already fully understand.
        var known = resp.Known && (req.Known || flow.RequestDataB64 is null);

        // For a diff outcome, compute git-style +/- line counts, existing endpoint vs the staged new
        // one, so the UI can show how big the change is.
        var (added, removed) = outcome == "diff" ? DiffCounts(_contentRoot, displayPath) : (0, 0);

        var (reqHeaders, reqHeadersRaw) = HeaderRedactor.Build(flow.RequestHeaders);
        var (respHeaders, respHeadersRaw) = HeaderRedactor.Build(flow.ResponseHeaders);

        return new DashboardFlow(
            Id: 0, Timestamp: "", Path: displayPath, Method: flow.Method, Status: flow.Status,
            RequestJson: req.Json, ResponseJson: resp.Json,
            ResponseB64: flow.ResponseBodyB64, RequestDataB64: flow.RequestDataB64,
            RequestType: req.Type, ResponseType: resp.Type, Known: known, Outcome: outcome,
            DiffAdded: added, DiffRemoved: removed,
            RequestJsonRaw: req.JsonRaw, ResponseJsonRaw: resp.JsonRaw, Url: flow.Url,
            RequestHeaders: reqHeaders, ResponseHeaders: respHeaders,
            RequestHeadersRaw: reqHeadersRaw, ResponseHeadersRaw: respHeadersRaw,
            ResponseIsAck: resp.Ack, ResponseText: resp.Text);
    }

    // Snapshot of the extractor's write tallies, used to derive a single flow's outcome.
    internal static (int wrote, int upd, int diff, int same, int loss) Snapshot(HarCounts c)
        => (c.Wrote, c.Upd, c.Diff, c.Same, c.Loss);

    // The single tally that changed between two snapshots is this flow's outcome.
    internal static string OutcomeDelta(
        (int wrote, int upd, int diff, int same, int loss) a,
        (int wrote, int upd, int diff, int same, int loss) b)
    {
        if (b.wrote > a.wrote) return "wrote";
        if (b.upd > a.upd) return "upd";
        if (b.diff > a.diff) return "diff";
        if (b.loss > a.loss) return "loss";
        if (b.same > a.same) return "same";
        return "";
    }

    // Git-style +/- line counts for a staged endpoint diff: lines present in the new staged file but
    // not the existing one are "added"; lines in the existing but not the new are "removed". Multiset
    // comparison, so duplicate lines count correctly.
    internal static (int added, int removed) DiffCounts(string contentRoot, string path)
    {
        try
        {
            var rel = Path.Combine(path.Replace('/', Path.DirectorySeparatorChar) + ".json");
            var existing = Path.Combine(contentRoot, "Endpoints", "default", rel);
            var staged = Path.Combine(contentRoot, "Endpoints", "staged", rel);
            if (!File.Exists(existing) || !File.Exists(staged)) return (0, 0);

            var oldBag = LineBag(File.ReadAllLines(existing));
            var newBag = LineBag(File.ReadAllLines(staged));

            int added = 0, removed = 0;
            foreach (var (l, n) in newBag) added += Math.Max(0, n - oldBag.GetValueOrDefault(l));
            foreach (var (l, n) in oldBag) removed += Math.Max(0, n - newBag.GetValueOrDefault(l));
            return (added, removed);
        }
        catch { return (0, 0); }
    }

    private static Dictionary<string, int> LineBag(string[] lines)
    {
        var bag = new Dictionary<string, int>();
        foreach (var l in lines) bag[l] = bag.GetValueOrDefault(l) + 1;
        return bag;
    }
}
