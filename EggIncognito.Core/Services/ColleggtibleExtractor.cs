using Ei;

namespace EggIncognito.Services;

public sealed record ColleggtibleDef(string Identifier, int Dimension, IReadOnlyList<double> TierValues);

public sealed record ColleggtibleExtract(
    IReadOnlyList<ColleggtibleDef> Eggs,
    IReadOnlyDictionary<string, string> ContractEggMap);

public static class ColleggtibleExtractor {
    public static ColleggtibleExtract FromPeriodicals(PeriodicalsResponse per) {
        var eggs = new List<ColleggtibleDef>();
        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        var contracts = per.Contracts;
        if (contracts is null) return new ColleggtibleExtract(eggs, map);

        foreach (var egg in contracts.CustomEggs) {
            if (string.IsNullOrEmpty(egg.Identifier) || egg.Buffs.Count == 0) continue;
            int dimension = (int)egg.Buffs[0].Dimension;
            double[] tiers = [.. egg.Buffs.Select(b => b.Value)];
            eggs.Add(new ColleggtibleDef(egg.Identifier, dimension, tiers));
        }

        foreach (var c in contracts.Contracts) {
            if (c.Egg != Egg.CustomEgg || string.IsNullOrEmpty(c.CustomEggId)) continue;
            if (string.IsNullOrEmpty(c.Identifier)) continue;
            map[c.Identifier] = c.CustomEggId;
        }

        return new ColleggtibleExtract(eggs, map);
    }

    public static ColleggtibleExtract FromPeriodicalsJson(string json) =>
        FromPeriodicals(PeriodicalsResponse.Parser.ParseJson(json));
}
