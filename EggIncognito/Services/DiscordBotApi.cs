namespace EggIncognito.Services;

public static class DiscordBotApi {
    public const string ApiBase = "https://discord.com/api/v10";

    public static HttpRequestMessage Request(HttpMethod method, string path, string token,
        HttpContent? content = null) {
        var req = new HttpRequestMessage(method, $"{ApiBase}/{path}");
        if (content is not null) req.Content = content;
        req.Headers.TryAddWithoutValidation("Authorization", $"Bot {token}");
        return req;
    }
}
