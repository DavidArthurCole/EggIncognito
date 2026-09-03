using System.Globalization;
using EggIdentity.Settings;

namespace EggIncognito.Services.Config;

public static class IndexedEnvLookup {
    public static Func<string, string?> For(SettingsRegistry registry) {
        ArgumentNullException.ThrowIfNull(registry);
        var listKeys = new HashSet<string>(
            registry.All
                .Where(d => d.Kind is SettingKind.StringList or SettingKind.CidrList)
                .Select(d => d.EnvKey),
            StringComparer.Ordinal);
        return envKey => listKeys.Contains(envKey) ? Collapse(envKey) : null;
    }

    private static string? Collapse(string envKey) {
        var parts = new List<string>();
        for (int i = 0; ; i++) {
            string? part = Environment.GetEnvironmentVariable(
                envKey + "__" + i.ToString(CultureInfo.InvariantCulture));
            if (string.IsNullOrWhiteSpace(part)) break;
            parts.Add(part.Trim());
        }

        return parts.Count == 0 ? null : string.Join(',', parts);
    }
}
