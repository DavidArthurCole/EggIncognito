using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using EggIncognito.Core.Models;

namespace EggIncognito.Runner.Posting;

// EventPoster sends a NewVersionEvent to the sync server, bearer-authed.
public sealed class EventPoster
{
    private readonly HttpClient _http;
    private readonly string _url;
    private readonly string _secret;

    public EventPoster(HttpClient http, string url, string secret)
    {
        _http = http;
        _url = url;
        _secret = secret;
    }

    public async Task PostAsync(NewVersionEvent evt)
    {
        var json = JsonSerializer.Serialize(evt);
        using var req = new HttpRequestMessage(HttpMethod.Post, _url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _secret);
        var resp = await _http.SendAsync(req);
        resp.EnsureSuccessStatusCode();
    }
}
