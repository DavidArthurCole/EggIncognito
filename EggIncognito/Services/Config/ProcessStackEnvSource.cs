using System.Collections;
using EggIdentity.Settings.AdminUi;

namespace EggIncognito.Services.Config;

public sealed class ProcessStackEnvSource : IStackEnvSource {
    private static readonly string[] Ignored = [
        "PATH", "HOME", "HOSTNAME", "PWD", "SHLVL", "TERM", "LANG", "USER", "TMPDIR", "TEMP", "TMP"
    ];

    public Task<IReadOnlyList<string>> GetStackKeysAsync(CancellationToken ct) {
        var keys = new List<string>();
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables()) {
            if (entry.Key is not string key || IsNoise(key)) continue;
            if (key.Contains("__", StringComparison.Ordinal)) {
                int idx = key.LastIndexOf("__", StringComparison.Ordinal);
                string tail = key[(idx + 2)..];
                if (tail.Length > 0 && tail.All(char.IsDigit)) key = key[..idx];
            }

            keys.Add(key);
        }

        return Task.FromResult<IReadOnlyList<string>>([.. keys.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)]);
    }

    private static bool IsNoise(string key) =>
        Ignored.Contains(key, StringComparer.Ordinal)
        || key.StartsWith("DOTNET_", StringComparison.Ordinal)
        || key.StartsWith("NUGET_", StringComparison.Ordinal)
        || key.StartsWith("LC_", StringComparison.Ordinal);
}
