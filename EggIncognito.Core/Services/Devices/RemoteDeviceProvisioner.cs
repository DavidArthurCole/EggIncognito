using System.Net.Http.Json;
using System.Text.Json;

namespace EggIncognito.Core.Services.Devices;

public sealed class RemoteDeviceProvisioner(IHttpClientFactory httpFactory, DeviceTransportConfig transport)
    : IDeviceProvisioner {
    public const string KindName = "remote";
    public const string BridgeRoot = "/api/devices/virtual/bridge";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string Kind => KindName;

    public ProvisionerCapabilities Capabilities =>
        ProvisionerCapabilities.Create | ProvisionerCapabilities.Destroy | ProvisionerCapabilities.List;

    public static bool IsRemoteKind(string? kind) =>
        string.Equals(kind, KindName, StringComparison.OrdinalIgnoreCase);

    public string? ConfigurationNote {
        get {
            if (transport.Mode != DeviceTransportMode.Remote) {
                return $"Devices:Virtual:Kind is '{KindName}' but DeviceTransport:Mode is '{transport.Mode}'. "
                       + "An instance provisioned on the remote host only has an address on that host's docker "
                       + "network, so adb must go through the device bridge too. Set DeviceTransport:Mode to Remote.";
            }

            if (string.IsNullOrWhiteSpace(transport.RemoteBaseUrl)) return "DeviceTransport:RemoteBaseUrl is not set";
            return string.IsNullOrWhiteSpace(transport.ApiKey) ? "DeviceTransport:ApiKey is not set" : null;
        }
    }

    public async Task<DeviceResult<ProvisionedInstance>> CreateAsync(ProvisionSpec spec, CancellationToken ct) {
        if (ConfigurationNote is { } missing) return DeviceResult<ProvisionedInstance>.Unsupported(missing);

        string? image = string.IsNullOrWhiteSpace(spec.Image) ? null : spec.Image;
        (var body, string? failure) =
            await SendAsync<CreateResponseBody>(HttpMethod.Post, "create", new CreateRequestBody(image), ct);
        if (body is null) return DeviceResult<ProvisionedInstance>.Unreachable(failure);
        if (!body.Ok) return DeviceResult<ProvisionedInstance>.Error(Refusal(body.Outcome, body.Note));

        return body.Instance is not { } instance
            ? DeviceResult<ProvisionedInstance>.Error("the provisioner bridge created nothing")
            : DeviceResult<ProvisionedInstance>.Success(Map(instance), body.Note);
    }

    public Task<DeviceResult> StartAsync(string instanceId, CancellationToken ct) =>
        Task.FromResult(DeviceResult.Unsupported("the provisioner bridge does not expose start"));

    public Task<DeviceResult> StopAsync(string instanceId, CancellationToken ct) =>
        Task.FromResult(DeviceResult.Unsupported("the provisioner bridge does not expose stop"));

    public async Task<DeviceResult> DestroyAsync(string instanceId, CancellationToken ct) {
        if (ConfigurationNote is { } missing) return DeviceResult.Unsupported(missing);

        (var body, string? failure) = await SendAsync<ActionResponseBody>(
            HttpMethod.Post, $"{Uri.EscapeDataString(instanceId)}/destroy", null, ct);
        if (body is null) return DeviceResult.Unreachable(failure);
        return body.Ok ? DeviceResult.Success(body.Note) : DeviceResult.Error(Refusal(body.Outcome, body.Note));
    }

    public async Task<DeviceResult<IReadOnlyList<ProvisionedInstance>>> ListAsync(CancellationToken ct) {
        if (ConfigurationNote is { } missing)
            return DeviceResult<IReadOnlyList<ProvisionedInstance>>.Unsupported(missing);

        (var body, string? failure) = await SendAsync<ListResponseBody>(HttpMethod.Get, "instances", null, ct);
        if (body is null) return DeviceResult<IReadOnlyList<ProvisionedInstance>>.Unreachable(failure);
        if (!body.Ok)
            return DeviceResult<IReadOnlyList<ProvisionedInstance>>.Error(Refusal(body.Outcome, body.Note));

        IReadOnlyList<ProvisionedInstance> mapped = [.. (body.Instances ?? []).Select(Map)];
        return DeviceResult<IReadOnlyList<ProvisionedInstance>>.Success(mapped, body.Note);
    }

    private async Task<(T? Body, string? Failure)> SendAsync<T>(
        HttpMethod method, string verb, object? payload, CancellationToken ct) {
        try {
            using var req = BuildRequest(method, verb);
            if (payload is not null) req.Content = JsonContent.Create(payload, options: JsonOptions);
            using var http = httpFactory.CreateClient();
            using var resp = await http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
                return (default, $"provisioner bridge {verb} {(int)resp.StatusCode} {resp.ReasonPhrase}");

            var parsed = await resp.Content.ReadFromJsonAsync<T>(JsonOptions, ct);
            return parsed is null ? (default, $"provisioner bridge {verb} empty response") : (parsed, null);
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            return (default, $"provisioner bridge {verb} error: {Describe(ex)}");
        } catch (OperationCanceledException ex) when (!ct.IsCancellationRequested) {
            return (default, $"provisioner bridge {verb} error: {Describe(ex)}");
        }
    }

    private static string Describe(Exception ex) {
        var parts = new List<string>();
        for (var e = ex; e is not null && parts.Count < 4; e = e.InnerException) {
            if (!string.IsNullOrWhiteSpace(e.Message)) parts.Add(e.Message);
        }

        return string.Join(" -> ", parts);
    }

    private HttpRequestMessage BuildRequest(HttpMethod method, string verb) {
        var req = new HttpRequestMessage(method, $"{transport.RemoteBaseUrl?.TrimEnd('/')}{BridgeRoot}/{verb}");
        if (!string.IsNullOrEmpty(transport.ApiKey)) req.Headers.Add("X-Api-Key", transport.ApiKey);
        return req;
    }

    private static string Refusal(string? outcome, string? note) =>
        note is { Length: > 0 } ? note : $"the provisioner bridge refused ({outcome ?? DeviceOutcomes.Error})";

    private static ProvisionedInstance Map(InstanceBody body) => new(
        body.InstanceId ?? "",
        body.Kind ?? KindName,
        body.Image ?? "",
        body.State ?? ProvisionStates.Failed,
        body.AdbSerial,
        body.HostRef,
        body.CreatedAt,
        body.Note,
        body.DeviceId);

    private sealed record CreateRequestBody(string? Image);

    private sealed record InstanceBody(
        string? InstanceId,
        string? Kind,
        string? Image,
        string? State,
        string? AdbSerial,
        string? HostRef,
        DateTimeOffset CreatedAt,
        string? Note,
        string? DeviceId);

    private sealed record ActionResponseBody(bool Ok, string? Outcome, string? Note);

    private sealed record CreateResponseBody(bool Ok, string? Outcome, string? Note, InstanceBody? Instance);

    private sealed record ListResponseBody(
        bool Ok, string? Outcome, string? Note, IReadOnlyList<InstanceBody>? Instances);
}
