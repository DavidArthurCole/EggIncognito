// Supporting types for EndpointExtractor: the per-flow decode results, the run-summary accumulator,
// the directory/type-map bundle, and the auto-write classification config. The pipeline lives in the
// library so it can be fed both in-process by the capture proxy and from a HAR file by RunFromHar.

using System.Reflection;
using Google.Protobuf;

namespace EggIncognito.Services;

// Run-summary accumulator. Tallies per-flow outcomes and collects the self-repair report.
public sealed class HarCounts
{
    public int Wrote, Upd, Diff, Same, Err, Loss;

    // Self-repair report. Learned = a type written to yaml; Flagged = detected but ambiguous tie,
    // needs a human.
    public readonly List<string> Learned = [];
    public readonly List<string> Flagged = [];
    public bool WroteYaml;
}

// Output directories + loaded type maps + the yaml editor for one extraction run.
public sealed record HarDirs(string OutDir, string StagedDir, string RequestsDir,
    IReadOnlyDictionary<string, string> TypeMap, IReadOnlyDictionary<string, string> RequestTypeMap,
    HashSet<string> RequestWrapped, RoutesYamlEditor Yaml);

// Outcome of the auto-write gate for a detected type.
public enum AutoWriteVerdict { Reject, Flag, Write }

// Result of decoding a captured request body.
//   Json            - decoded request JSON for the .request.json dump, or null.
//   DetectedType    - Write-eligible inner type to backfill into yaml, or null.
//   DetectedWrapped - whether the request was AuthenticatedMessage-wrapped on the wire.
//   FlagNote        - set when a type was detected but the gate said Flag.
public sealed record RequestDecode(string? Json, string? DetectedType, bool DetectedWrapped, string? FlagNote)
{
    // True when the captured request had no body proto.
    public bool EmptyBody { get; init; }
}

// Everything learned from one HAR entry. AutoResponseType and scores are set only when the response
// type was not already known and had to be auto-detected.
public sealed record DecodedEntry(
    string Path, string Json, RequestDecode Request,
    string? AutoResponseType, int RespBestScore, int RespSecondScore);

public static class ExtractorConfig
{
    internal static readonly HashSet<string> AlwaysSkip = new(StringComparer.Ordinal)
    {
        "ei/get_config",
    };

    public static readonly Assembly EiAssembly = typeof(Ei.AuthenticatedMessage).Assembly;

    // TryParseAs adds this bonus when a candidate re-serializes to the exact original bytes.
    public const int ExactBonus = 1000;

    // Auto-write only when the winner is an exact round-trip and has strictly more fields than the
    // runner-up. A true tie, equal field counts among exact matches, is flagged.
    public static AutoWriteVerdict ClassifyAutoWrite(int bestScore, int secondBestScore)
    {
        if (bestScore < ExactBonus) return AutoWriteVerdict.Reject; // winner did not round-trip
        if (secondBestScore < ExactBonus) return AutoWriteVerdict.Write; // sole exact, decisive
        var bestFields = bestScore - ExactBonus;
        var secondFields = secondBestScore - ExactBonus;
        return bestFields > secondFields ? AutoWriteVerdict.Write : AutoWriteVerdict.Flag;
    }
}
