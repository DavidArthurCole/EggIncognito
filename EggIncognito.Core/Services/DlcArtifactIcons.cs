using Ei;

namespace EggIncognito.Core.Services;

public static class DlcArtifactIcons {
    public static IReadOnlyDictionary<string, string> FromConfigJson(string json) =>
        FromConfig(ConfigResponse.Parser.ParseJson(json));

    public static IReadOnlyDictionary<string, string> FromConfig(ConfigResponse cfg) {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (cfg.DlcCatalog is null) return map;

        foreach (var item in cfg.DlcCatalog.Items) {
            if (item.Directory != "artifacts" || string.IsNullOrEmpty(item.Name)) continue;
            string? ext = string.IsNullOrEmpty(item.Ext) ? "png" : item.Ext;
            map[item.Name] = $"{item.Name}.{ext}";
        }

        return map;
    }
}
