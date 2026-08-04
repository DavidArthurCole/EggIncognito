namespace EggIncognito.Tests;

public sealed class StubHttpFactory(HttpMessageHandler? handler = null) : IHttpClientFactory {
    public string? LastName { get; private set; }

    public HttpClient CreateClient(string name) {
        LastName = name;
        return handler is null ? new HttpClient() : new HttpClient(handler, false);
    }
}
