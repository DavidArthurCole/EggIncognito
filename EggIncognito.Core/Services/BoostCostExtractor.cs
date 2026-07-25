using Ei;

namespace EggIncognito.Services;

public readonly record struct BoostCost(int Price, int TokenPrice, double SeRequired);

public static class BoostCostExtractor {
    public static IReadOnlyDictionary<string, BoostCost> FromConfig(ConfigResponse cfg) {
        var result = new Dictionary<string, BoostCost>(StringComparer.Ordinal);
        var boostsConfig = cfg.LiveConfig?.BoostsConfig;
        if (boostsConfig is null) return result;

        foreach (var ic in boostsConfig.ItemConfigs) {
            if (string.IsNullOrEmpty(ic.BoostId)) continue;
            result[ic.BoostId] = new BoostCost((int)ic.Price, (int)ic.TokenPrice, ic.SeRequired);
        }

        return result;
    }

    public static IReadOnlyDictionary<string, BoostCost> FromConfigJson(string json) =>
        FromConfig(ConfigResponse.Parser.ParseJson(json));
}
