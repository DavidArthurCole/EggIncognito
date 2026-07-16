using EggIncognito.Services.Metrics;

namespace EggIncognito.Services;

public static class SelfCallClient
{
    public static HttpClient Create(IHttpClientFactory factory, string? baseAddress, string? cookie)
    {
        var c = factory.CreateClient();
        if (!string.IsNullOrEmpty(baseAddress)) c.BaseAddress = new Uri(baseAddress);
        if (!string.IsNullOrEmpty(cookie)) c.DefaultRequestHeaders.Add("Cookie", cookie);
        c.DefaultRequestHeaders.Add(RequestBucketClassifier.SelfCallHeader, "1");
        return c;
    }
}
