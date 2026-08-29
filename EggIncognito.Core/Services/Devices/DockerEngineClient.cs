using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EggIncognito.Core.Services.Devices;

public sealed record DockerContainer(
    string Id,
    string Name,
    string Image,
    string State,
    string Status,
    DateTimeOffset CreatedAt,
    IReadOnlyDictionary<string, string> Labels,
    string? IpAddress = null);

public sealed record DockerInspect(
    string Id,
    string Name,
    string Image,
    bool Running,
    string Status,
    DateTimeOffset? StartedAt,
    IReadOnlyList<string> Networks,
    IReadOnlyDictionary<string, string> Labels,
    string? IpAddress = null);

public sealed record DockerCreateSpec(
    string Name,
    string Image,
    IReadOnlyList<string> Cmd,
    IReadOnlyList<string> Binds,
    string? Network,
    IReadOnlyDictionary<string, string> Labels,
    bool Privileged = true,
    string RestartPolicy = "unless-stopped");

public sealed partial class DockerEngineClient : IDisposable {
    public const string HostNetwork = "host";
    private static readonly string[] ReservedNetworks = ["bridge", "host", "none"];
    private readonly HttpClient _http;
    private readonly SocketsHttpHandler _handler;

    public DockerEngineClient(string socketPath) {
        SocketPath = socketPath;
        _handler = new SocketsHttpHandler {
            ConnectCallback = async (_, ct) => {
                var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                try {
                    await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), ct);
                    return new NetworkStream(socket, true);
                } catch {
                    socket.Dispose();
                    throw;
                }
            }
        };
        _http = new HttpClient(_handler) {
            BaseAddress = new Uri("http://docker/"),
            Timeout = TimeSpan.FromSeconds(90)
        };
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public string SocketPath { get; }

    public bool SocketPresent => !OperatingSystem.IsWindows() && File.Exists(SocketPath);

    public void Dispose() {
        _http.Dispose();
        _handler.Dispose();
    }

    public async Task<DeviceResult> PingAsync(CancellationToken ct) {
        if (!SocketPresent) return DeviceResult.Unsupported($"docker socket {SocketPath} is not present");
        var res = await SendAsync(HttpMethod.Get, "_ping", null, ct);
        return res.Ok ? DeviceResult.Success() : new DeviceResult(res.Outcome, res.Note);
    }

    public async Task<DeviceResult<IReadOnlyList<DockerContainer>>> ListAsync(
        string? label, CancellationToken ct) {
        string path = "containers/json?all=1";
        if (!string.IsNullOrEmpty(label)) {
            string filters = $"{{\"label\":[\"{label}\"]}}";
            path += "&filters=" + Uri.EscapeDataString(filters);
        }

        var res = await SendAsync(HttpMethod.Get, path, null, ct);
        if (!res.Ok) return new DeviceResult<IReadOnlyList<DockerContainer>>(res.Outcome, null, res.Note);

        try {
            using var doc = JsonDocument.Parse(res.Value ?? "[]");
            var list = new List<DockerContainer>();
            foreach (var el in doc.RootElement.EnumerateArray()) list.Add(ReadSummary(el));
            return DeviceResult<IReadOnlyList<DockerContainer>>.Success(list);
        } catch (JsonException ex) {
            return DeviceResult<IReadOnlyList<DockerContainer>>.Error($"unreadable container list: {ex.Message}");
        }
    }

    public async Task<DeviceResult<DockerInspect>> InspectAsync(string idOrName, CancellationToken ct) {
        var res = await SendAsync(HttpMethod.Get, $"containers/{Uri.EscapeDataString(idOrName)}/json", null, ct);
        if (!res.Ok) return new DeviceResult<DockerInspect>(res.Outcome, null, res.Note);

        try {
            using var doc = JsonDocument.Parse(res.Value ?? "{}");
            return DeviceResult<DockerInspect>.Success(ReadInspect(doc.RootElement));
        } catch (JsonException ex) {
            return DeviceResult<DockerInspect>.Error($"unreadable inspect payload: {ex.Message}");
        }
    }

    public async Task<DeviceResult<string>> CreateAsync(DockerCreateSpec spec, CancellationToken ct) {
        var hostConfig = new Dictionary<string, object?> {
            ["Privileged"] = spec.Privileged,
            ["Binds"] = spec.Binds,
            ["RestartPolicy"] = new Dictionary<string, object?> { ["Name"] = spec.RestartPolicy }
        };
        if (!string.IsNullOrEmpty(spec.Network)) hostConfig["NetworkMode"] = spec.Network;

        var body = new Dictionary<string, object?> {
            ["Image"] = spec.Image,
            ["Cmd"] = spec.Cmd,
            ["Labels"] = spec.Labels,
            ["HostConfig"] = hostConfig
        };

        var res = await SendAsync(HttpMethod.Post,
            $"containers/create?name={Uri.EscapeDataString(spec.Name)}", JsonSerializer.Serialize(body), ct);
        if (!res.Ok) return new DeviceResult<string>(res.Outcome, null, res.Note);

        try {
            using var doc = JsonDocument.Parse(res.Value ?? "{}");
            string? id = doc.RootElement.TryGetProperty("Id", out var idEl) ? idEl.GetString() : null;
            return string.IsNullOrEmpty(id)
                ? DeviceResult<string>.Error("docker returned no container id")
                : DeviceResult<string>.Success(id);
        } catch (JsonException ex) {
            return DeviceResult<string>.Error($"unreadable create payload: {ex.Message}");
        }
    }

    public async Task<DeviceResult> StartAsync(string id, CancellationToken ct) =>
        Plain(await SendAsync(HttpMethod.Post, $"containers/{Uri.EscapeDataString(id)}/start", null, ct));

    public async Task<DeviceResult> StopAsync(string id, int timeoutSeconds, CancellationToken ct) {
        string t = timeoutSeconds.ToString(CultureInfo.InvariantCulture);
        return Plain(await SendAsync(HttpMethod.Post, $"containers/{Uri.EscapeDataString(id)}/stop?t={t}", null, ct));
    }

    public async Task<DeviceResult> RemoveAsync(string id, CancellationToken ct) =>
        Plain(await SendAsync(HttpMethod.Delete, $"containers/{Uri.EscapeDataString(id)}?force=1&v=1", null, ct, true));

    public async Task<DeviceResult> RemoveVolumeAsync(string name, CancellationToken ct) =>
        Plain(await SendAsync(HttpMethod.Delete, $"volumes/{Uri.EscapeDataString(name)}?force=1", null, ct, true));

    public async Task<DeviceResult<string>> SelfNetworkAsync(CancellationToken ct) {
        if (!SocketPresent) return DeviceResult<string>.Unsupported($"docker socket {SocketPath} is not present");

        var tried = new List<string>();
        foreach (string candidate in SelfIdCandidates()) {
            if (string.IsNullOrWhiteSpace(candidate) || tried.Contains(candidate, StringComparer.Ordinal)) continue;
            tried.Add(candidate);
            var inspect = await InspectAsync(candidate, ct);
            if (!inspect.Ok || inspect.Value is not { } self) continue;

            string? network = self.Networks
                .FirstOrDefault(n => !ReservedNetworks.Contains(n, StringComparer.OrdinalIgnoreCase));
            if (network is not null) return DeviceResult<string>.Success(network);

            if (self.Networks.Contains(HostNetwork, StringComparer.OrdinalIgnoreCase))
                return DeviceResult<string>.Success(HostNetwork);

            return DeviceResult<string>.Error(
                $"this container is only on {string.Join(", ", self.Networks)}; attach it to a user-defined docker " +
                "network or run it with network_mode: host");
        }

        return DeviceResult<string>.Error(
            "could not identify this container from the docker daemon " +
            $"(tried {(tried.Count == 0 ? "no candidates" : string.Join(", ", tried))}); " +
            "the app must run as a container for virtual devices to work");
    }

    private static IEnumerable<string> SelfIdCandidates() {
        yield return Environment.MachineName;
        foreach (string id in ReadProcIds("/proc/self/mountinfo")) yield return id;
        foreach (string id in ReadProcIds("/proc/self/cgroup")) yield return id;
    }

    private static List<string> ReadProcIds(string path) {
        string text;
        try {
            if (!File.Exists(path)) return [];
            text = File.ReadAllText(path);
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            return [];
        }

        return [.. ContainerId().Matches(text).Select(m => m.Value).Distinct(StringComparer.Ordinal)];
    }

    [GeneratedRegex("[0-9a-f]{64}")]
    private static partial Regex ContainerId();

    private static DeviceResult Plain(DeviceResult<string> res) =>
        res.Ok ? DeviceResult.Success(res.Note) : new DeviceResult(res.Outcome, res.Note);

    private static DockerContainer ReadSummary(JsonElement el) {
        string name = el.TryGetProperty("Names", out var names) && names.ValueKind == JsonValueKind.Array
            ? names.EnumerateArray().Select(n => n.GetString() ?? "").FirstOrDefault()?.TrimStart('/') ?? ""
            : "";
        long created = el.TryGetProperty("Created", out var c) && c.TryGetInt64(out long v) ? v : 0;
        return new DockerContainer(
            Str(el, "Id"),
            name,
            Str(el, "Image"),
            Str(el, "State"),
            Str(el, "Status"),
            DateTimeOffset.FromUnixTimeSeconds(created),
            Labels(el.TryGetProperty("Labels", out var lb) ? lb : default),
            FirstIp(el));
    }

    private static string? FirstIp(JsonElement root) {
        if (!root.TryGetProperty("NetworkSettings", out var ns)
            || !ns.TryGetProperty("Networks", out var nets)
            || nets.ValueKind != JsonValueKind.Object)
            return null;

        foreach (var n in nets.EnumerateObject()) {
            string ip = Str(n.Value, "IPAddress");
            if (ip.Length > 0) return ip;
        }

        return null;
    }

    private static DockerInspect ReadInspect(JsonElement root) {
        var state = root.TryGetProperty("State", out var s) ? s : default;
        var config = root.TryGetProperty("Config", out var cfg) ? cfg : default;
        bool running = state.ValueKind == JsonValueKind.Object
                       && state.TryGetProperty("Running", out var r)
                       && r.ValueKind == JsonValueKind.True;

        DateTimeOffset? startedAt = null;
        if (state.ValueKind == JsonValueKind.Object
            && state.TryGetProperty("StartedAt", out var sa)
            && DateTimeOffset.TryParse(sa.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind,
                out var parsed)
            && parsed.Year > 1)
            startedAt = parsed;

        var networks = new List<string>();
        if (root.TryGetProperty("NetworkSettings", out var ns)
            && ns.TryGetProperty("Networks", out var nets)
            && nets.ValueKind == JsonValueKind.Object) {
            foreach (var n in nets.EnumerateObject()) networks.Add(n.Name);
        }

        return new DockerInspect(
            Str(root, "Id"),
            Str(root, "Name").TrimStart('/'),
            config.ValueKind == JsonValueKind.Object ? Str(config, "Image") : "",
            running,
            state.ValueKind == JsonValueKind.Object ? Str(state, "Status") : "",
            startedAt,
            networks,
            Labels(config.ValueKind == JsonValueKind.Object && config.TryGetProperty("Labels", out var lb)
                ? lb
                : default),
            FirstIp(root));
    }

    private static string Str(JsonElement el, string name) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? ""
            : "";

    private static Dictionary<string, string> Labels(JsonElement el) {
        if (el.ValueKind != JsonValueKind.Object) return [with(StringComparer.Ordinal)];
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var p in el.EnumerateObject()) map[p.Name] = p.Value.GetString() ?? "";
        return map;
    }

    private async Task<DeviceResult<string>> SendAsync(
        HttpMethod method, string path, string? json, CancellationToken ct, bool allowMissing = false) {
        if (!SocketPresent) return DeviceResult<string>.Unsupported($"docker socket {SocketPath} is not present");

        using var req = new HttpRequestMessage(method, path);
        if (json is not null) req.Content = new StringContent(json, Encoding.UTF8, "application/json");

        try {
            using var res = await _http.SendAsync(req, ct);
            string body = await res.Content.ReadAsStringAsync(ct);
            if (res.IsSuccessStatusCode) return DeviceResult<string>.Success(body);
            if (res.StatusCode == HttpStatusCode.NotFound && allowMissing)
                return DeviceResult<string>.Success("", "already gone");
            return DeviceResult<string>.Error($"docker {(int)res.StatusCode}: {Trim(body)}");
        } catch (HttpRequestException ex) {
            return DeviceResult<string>.Unsupported($"docker socket unreachable: {ex.Message}");
        } catch (SocketException ex) {
            return DeviceResult<string>.Unsupported($"docker socket unreachable: {ex.Message}");
        } catch (IOException ex) {
            return DeviceResult<string>.Unsupported($"docker socket unreachable: {ex.Message}");
        } catch (TaskCanceledException ex) when (!ct.IsCancellationRequested) {
            return DeviceResult<string>.Unreachable($"docker request timed out: {ex.Message}");
        }
    }

    private static string Trim(string body) {
        string one = body.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return one.Length <= 300 ? one : one[..300];
    }
}
