using System.Net.Http.Headers;

namespace EggIncognito.Services.Devices;

public sealed record DeviceProbeDto(
    string Id, bool Reachable, string? InstalledAppVersion, string? InstalledBuild,
    string? LatestAvailable, string Result, string? Note, DateTimeOffset ProbedAt);

public interface IDeviceAgentClient {
    bool Enabled { get; }
    Task<DeviceProbeDto?> ProbeAsync(string id, CancellationToken ct);
    Task<int> ProbeAllAsync(CancellationToken ct);
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

    public async Task<DeviceProbeDto?> ProbeAsync(string id, CancellationToken ct) {
        try {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/devices/{id}/probe");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _secret);
            using var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadFromJsonAsync<DeviceProbeDto>(ct);
        } catch (HttpRequestException) {
            return null;
        } catch (OperationCanceledException) when (!ct.IsCancellationRequested) {
            return null;
        }
    }

    public async Task<int> ProbeAllAsync(CancellationToken ct) {
        try {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/devices/probe-all");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _secret);
            using var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return 0;
            var body = await resp.Content.ReadFromJsonAsync<ProbeAllResponse>(ct);
            return body?.Probed ?? 0;
        } catch (HttpRequestException) {
            return 0;
        } catch (OperationCanceledException) when (!ct.IsCancellationRequested) {
            return 0;
        }
    }

    private sealed record ProbeAllResponse(int Probed);
}
