using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using EggIdentity.Contract;

namespace EggIncognito.Bot;

public sealed record DeployResult(
    bool Ok,
    bool AlreadyUpToDate,
    string? Tail,
    string? FromHash,
    string? ToHash,
    string? FromUrl = null,
    string? ToUrl = null) {
    public static DeployResult Failure(string tail) => new(false, false, tail, null, null);
}

public sealed class DeployAgentClient(string url, string secret) {
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(120) };

    public async Task<DeployResult> DeployAsync(CancellationToken ct = default) {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            return DeployResult.Failure($"Deploy agent URL is invalid: \"{url}\".");
        try {
            using var req = new HttpRequestMessage(HttpMethod.Post, uri);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
            using var resp = await Http.SendAsync(req, ct);
            return resp.StatusCode == HttpStatusCode.Conflict
                ? DeployResult.Failure("A deploy is already in progress.")
                : !resp.IsSuccessStatusCode
                    ? DeployResult.Failure($"Deploy agent returned {(int)resp.StatusCode} {resp.ReasonPhrase}.")
                    : Parse(await resp.Content.ReadAsStringAsync(ct));
        } catch (TaskCanceledException) {
            return DeployResult.Failure("Deploy agent timed out.");
        } catch (HttpRequestException ex) {
            return DeployResult.Failure($"Could not reach deploy agent: {ex.Message}");
        }
    }


    public static DeployResult Parse(string json) {
        try {
            var r = JsonSerializer.Deserialize<DeployResponse>(json);
            return r is null
                ? DeployResult.Failure("Could not decode deploy agent response.")
                : new DeployResult(r.Ok, r.AlreadyUpToDate, r.Tail, r.FromHash, r.ToHash, r.FromUrl, r.ToUrl);
        } catch (JsonException) {
            return DeployResult.Failure("Could not decode deploy agent response.");
        }
    }
}
