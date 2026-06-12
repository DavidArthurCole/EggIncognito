using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace EggIncognito.Bot;

// Result of a deploy-agent call, mirroring synckit's contract.DeployResponse wire JSON.
// Failures (HTTP errors, timeouts, bad JSON) are mapped to Ok=false with a human Tail, so the
// router only ever renders three shapes: up-to-date, deployed, failed.
public sealed record DeployResult(bool Ok, bool AlreadyUpToDate, string? Tail, string? FromHash, string? ToHash)
{
    public static DeployResult Failure(string tail) => new(false, false, tail, null, null);
}

// Talks to the host-side synckit-agent: POST {url} with a bearer secret, decode the JSON reply.
// System.Text.Json is correct here - this is the synckit HTTP contract, not endpoint proto JSON.
public sealed class DeployAgentClient(string url, string secret)
{
    // 120s: a docker pull of a fresh image can take a while; matches the ledger bot's client timeout.
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(120) };

    public async Task<DeployResult> DeployAsync(CancellationToken ct = default)
    {
        // A misconfigured stack env var (relative URI, wrong scheme) must surface in the embed,
        // not as an unhandled exception in the router.
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            return DeployResult.Failure($"Deploy agent URL is invalid: \"{url}\".");
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, uri);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
            using var resp = await Http.SendAsync(req, ct);
            if (resp.StatusCode == HttpStatusCode.Conflict)
                return DeployResult.Failure("A deploy is already in progress.");
            if (!resp.IsSuccessStatusCode)
                return DeployResult.Failure($"Deploy agent returned {(int)resp.StatusCode} {resp.ReasonPhrase}.");
            return Parse(await resp.Content.ReadAsStringAsync(ct));
        }
        catch (TaskCanceledException) { return DeployResult.Failure("Deploy agent timed out."); }
        catch (HttpRequestException ex) { return DeployResult.Failure($"Could not reach deploy agent: {ex.Message}"); }
    }

    // Pure JSON-to-result mapping, unit-tested without HTTP.
    public static DeployResult Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return DeployResult.Failure("Could not decode deploy agent response.");
            return new DeployResult(Bool(root, "ok"), Bool(root, "alreadyUpToDate"),
                Str(root, "tail"), Str(root, "fromHash"), Str(root, "toHash"));
        }
        catch (JsonException) { return DeployResult.Failure("Could not decode deploy agent response."); }
    }

    private static bool Bool(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    private static string? Str(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
