using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace EggIncognito.Core.Services.Devices;

public sealed class RemoteDeviceConnection(HttpClient http, DeviceTransportConfig cfg, DeviceTarget target)
    : IDeviceConnection {
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string Platform => target.Platform;

    public async Task<ProcessResult> ShellAsync(string command, CancellationToken ct) {
        try {
            using var req = BuildRequest("shell");
            req.Content = JsonContent.Create(new ShellRequestBody(command), options: JsonOptions);
            using var resp = await http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) {
                return new ProcessResult(-1, "", $"transport shell {(int)resp.StatusCode} {resp.ReasonPhrase}");
            }

            var body = await resp.Content.ReadFromJsonAsync<ShellResponseBody>(JsonOptions, ct);
            return body is null
                ? new ProcessResult(-1, "", "transport shell empty response")
                : new ProcessResult(body.Exit, body.Stdout ?? "", body.Stderr ?? "");
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            return new ProcessResult(-1, "", $"transport shell error: {ex.Message}");
        } catch (OperationCanceledException ex) when (!ct.IsCancellationRequested) {
            return new ProcessResult(-1, "", $"transport shell error: {ex.Message}");
        }
    }

    public async Task<byte[]?> PullBytesAsync(string remotePath, CancellationToken ct) {
        try {
            using var req = BuildRequest("pull");
            req.Content = JsonContent.Create(new PullRequestBody(remotePath), options: JsonOptions);
            using var resp = await http.SendAsync(req, ct);
            if (resp.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.NoContent) {
                return null;
            }

            return resp.IsSuccessStatusCode ? await resp.Content.ReadAsByteArrayAsync(ct) : null;
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            return null;
        } catch (OperationCanceledException) when (!ct.IsCancellationRequested) {
            return null;
        }
    }

    public async Task<bool> PushFileAsync(string localPath, string remotePath, CancellationToken ct) {
        if (!File.Exists(localPath)) {
            return false;
        }

        try {
            byte[] bytes = await File.ReadAllBytesAsync(localPath, ct);
            using var req = BuildRequest("push");
            var pushBody = new PushRequestBody(remotePath, Convert.ToBase64String(bytes));
            req.Content = JsonContent.Create(pushBody, options: JsonOptions);
            using var resp = await http.SendAsync(req, ct);
            return resp.IsSuccessStatusCode;
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            return false;
        } catch (OperationCanceledException) when (!ct.IsCancellationRequested) {
            return false;
        }
    }

    private HttpRequestMessage BuildRequest(string verb) {
        var req = new HttpRequestMessage(HttpMethod.Post,
            $"{cfg.RemoteBaseUrl?.TrimEnd('/')}/api/devices/{target.Id}/transport/{verb}");
        if (!string.IsNullOrEmpty(cfg.ApiKey)) {
            req.Headers.Add("X-Api-Key", cfg.ApiKey);
        }

        return req;
    }

    private sealed record ShellRequestBody(string Cmd);

    private sealed record PullRequestBody(string Path);

    private sealed record PushRequestBody(string Path, string Base64);

    private sealed record ShellResponseBody(int Exit, string? Stdout, string? Stderr);
}
