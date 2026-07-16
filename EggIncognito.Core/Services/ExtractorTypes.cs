

using System.Reflection;
using Google.Protobuf;

namespace EggIncognito.Services;
public sealed class HarCounts
{
    public int Wrote, Upd, Diff, Same, Err, Loss;

   
   
    public readonly List<string> Learned = [];
    public readonly List<string> Flagged = [];
    public bool WroteYaml;
}
public sealed record HarDirs(string OutDir, string StagedDir, string RequestsDir,
    IReadOnlyDictionary<string, string> TypeMap, IReadOnlyDictionary<string, string> RequestTypeMap,
    HashSet<string> RequestWrapped, RoutesYamlEditor Yaml);
public enum AutoWriteVerdict { Reject, Flag, Write }


public sealed record RequestDecode(string? Json, string? DetectedType, bool DetectedWrapped, string? FlagNote)
{
   
    public bool EmptyBody { get; init; }
}

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

   
    public const int ExactBonus = 1000;

   
   
    public static AutoWriteVerdict ClassifyAutoWrite(int bestScore, int secondBestScore)
    {
        if (bestScore < ExactBonus) return AutoWriteVerdict.Reject;
        if (secondBestScore < ExactBonus) return AutoWriteVerdict.Write;
        var bestFields = bestScore - ExactBonus;
        var secondFields = secondBestScore - ExactBonus;
        return bestFields > secondFields ? AutoWriteVerdict.Write : AutoWriteVerdict.Flag;
    }
}
