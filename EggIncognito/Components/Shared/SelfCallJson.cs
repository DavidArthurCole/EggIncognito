using System.Text.Json;

namespace EggIncognito.Components.Shared;

public static class SelfCallJson {
    public static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    public static async Task<List<T>> ListAsync<T>(this HttpClient client, string url, ILogger? log = null,
        CancellationToken ct = default) {
        try {
            var response = await client.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode) {
                log?.LogDebug("self call {Url} returned {Status}", url, (int)response.StatusCode);
                return [];
            }

            return await response.Content.ReadFromJsonAsync<List<T>>(Web, ct) ?? [];
        } catch (Exception ex) {
            log?.LogDebug(ex, "self call {Url} failed", url);
            return [];
        }
    }

    public static async Task<T?> OneAsync<T>(this HttpClient client, string url, ILogger? log = null,
        CancellationToken ct = default) {
        try {
            var response = await client.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode) {
                log?.LogDebug("self call {Url} returned {Status}", url, (int)response.StatusCode);
                return default;
            }

            return await response.Content.ReadFromJsonAsync<T>(Web, ct);
        } catch (Exception ex) {
            log?.LogDebug(ex, "self call {Url} failed", url);
            return default;
        }
    }
}
