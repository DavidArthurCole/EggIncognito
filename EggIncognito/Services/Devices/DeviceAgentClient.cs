using System.Net.Http.Headers;

namespace EggIncognito.Services.Devices;

public sealed record DeviceProbeDto(
    string Id, bool Reachable, string? InstalledAppVersion, string? InstalledBuild,
    string? LatestAvailable, string Result, string? Note, DateTimeOffset ProbedAt);

public interface IDeviceAgentClient {
    bool Enabled { get; }
    Task<DeviceProbeDto?> ProbeAsync(string id, CancellationToken ct);
    Task<int> ProbeAllAsync(CancellationToken ct);
    Task<bool> PokeAsync(string? id, bool force, CancellationToken ct);
}

public sealed class DeviceAgentClient : IDeviceAgentClient {
    private readonly HttpClient _http;
    private readonly string? _baseUrl;
    private readonly string _secret;

    public DeviceAgentClient(HttpClient http, IConfiguration config) {
        _http = http;
        _baseUrl = config["DeviceAgent:Url"];
        _secret = config["DeviceAgent:Secret"] ?? "";
    }

    public bool Enabled => !string.IsNullOrWhiteSpace(_baseUrl) && _secret.Length > 0;

    public Task<DeviceProbeDto?> ProbeAsync(string id, CancellationToken ct) =>
        PostAsync<DeviceProbeDto>($"/devices/{id}/probe", ct);

    public async Task<int> ProbeAllAsync(CancellationToken ct) =>
        (await PostAsync<ProbeAllResponse>("/devices/probe-all", ct))?.Probed ?? 0;

    public async Task<bool> PokeAsync(string? id, bool force, CancellationToken ct) {
        if (!Enabled) return false;
        string path = string.IsNullOrEmpty(id) ? "/devices/poke-all" : $"/devices/{id}/poke";
        return await PostAsync<PokeResponse>(path, ct, new ForceBody(force)) is not null;
    }

    private async Task<T?> PostAsync<T>(string path, CancellationToken ct, ForceBody? body = null) {
        try {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}{path}");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _secret);
            if (body is not null) req.Content = JsonContent.Create(body);
            using var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return default;
            return await resp.Content.ReadFromJsonAsync<T>(ct);
        } catch (HttpRequestException) {
            return default;
        } catch (OperationCanceledException) when (!ct.IsCancellationRequested) {
            return default;
        }
    }

    private sealed record ForceBody(bool Force);

    private sealed record ProbeAllResponse(int Probed);

    private sealed record PokeResponse(bool Queued);
}
