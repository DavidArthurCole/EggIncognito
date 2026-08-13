using System.Text;
using System.Text.Json;
using Microsoft.JSInterop;

namespace EggIncognito.Components.Shared;

public sealed record BrowserResponse(
    int Status,
    bool Ok,
    string ContentType,
    string Body,
    bool Binary,
    long ByteSize,
    string? FileName) {
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static BrowserResponse Failed(string why) {
        return new BrowserResponse(0, false, "", why, false, 0, null);
    }

    public T? Json<T>() {
        if (Binary || string.IsNullOrWhiteSpace(Body)) return default;
        try {
            return JsonSerializer.Deserialize<T>(Body, Options);
        } catch (JsonException) {
            return default;
        }
    }

    public byte[] Bytes() {
        return Binary ? Convert.FromBase64String(Body) : Encoding.UTF8.GetBytes(Body);
    }

    public string Describe() {
        if (Json<ErrorBody>()?.Error is { Length: > 0 } detail) return detail;
        if (Status > 0) return $"HTTP {Status}";
        return Body.Length > 0 ? Body : "request failed";
    }

    private sealed record ErrorBody(string? Error);
}

public sealed class BrowserApi(IJSObjectReference module) {
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public Task<BrowserResponse> GetAsync(string url) {
        return SendAsync("GET", url);
    }

    public Task<BrowserResponse> PostAsync(string url) {
        return SendAsync("POST", url);
    }

    public Task<BrowserResponse> DeleteAsync(string url) {
        return SendAsync("DELETE", url);
    }

    public Task<BrowserResponse> PostJsonAsync<T>(string url, T body) {
        return SendAsync("POST", url, JsonSerializer.Serialize(body, Options));
    }

    public Task<BrowserResponse> PutJsonAsync<T>(string url, T body) {
        return SendAsync("PUT", url, JsonSerializer.Serialize(body, Options));
    }

    public Task<BrowserResponse> SendAsync(string method, string url, string? body = null,
        string contentType = "application/json") {
        return InvokeAsync("send", [method, url, body, contentType]);
    }

    public Task<BrowserResponse> SendFileAsync(string method, string url, string field, string fileName,
        byte[] bytes) {
        return InvokeAsync("sendFile", [method, url, field, fileName, Convert.ToBase64String(bytes)]);
    }

    private async Task<BrowserResponse> InvokeAsync(string function, object?[] args) {
        try {
            return await module.InvokeAsync<BrowserResponse?>(function, args)
                   ?? BrowserResponse.Failed("no response from the browser");
        } catch (JSDisconnectedException) {
            return BrowserResponse.Failed("browser disconnected");
        } catch (ObjectDisposedException) {
            return BrowserResponse.Failed("browser disconnected");
        } catch (TaskCanceledException) {
            return BrowserResponse.Failed("browser call timed out");
        } catch (JSException ex) {
            return BrowserResponse.Failed(ex.Message);
        }
    }
}
