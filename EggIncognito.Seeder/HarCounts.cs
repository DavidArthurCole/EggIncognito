using System.Reflection;

namespace EggIncognito.Seeder;

sealed class HarCounts { public int Wrote, Upd, Diff, Same, Err, Loss; }

record HarDirs(string OutDir, string StagedDir, string RequestsDir, IReadOnlyDictionary<string, string> TypeMap, IReadOnlyDictionary<string, string> RequestTypeMap);

static class SeederConfig
{
    public static readonly HashSet<string> AlwaysSkip = new(StringComparer.Ordinal)
    {
        "ei/get_config",
    };

    public static readonly Assembly EiAssembly = typeof(Ei.AuthenticatedMessage).Assembly;
}
