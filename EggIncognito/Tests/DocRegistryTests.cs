using EggIncognito.Core.Services;
using EggIncognito.Services;

namespace EggIncognito.Tests;

public sealed class DocRegistryTests : IDisposable {
    private readonly TempDir _tmp = new();

    public void Dispose() => _tmp.Dispose();

    private const string YamlText = """
                                    routes:
                                      - path: ei/first_contact_secure
                                        request: EggIncFirstContactRequest
                                        requestWrapped: true
                                        response: EggIncFirstContactResponse
                                        responseWrapped: true
                                      - path: ei/get_periodicals
                                        request: GetPeriodicalsRequest
                                        response: PeriodicalsResponse
                                    """;

    private DocRegistry Build() {
        string path = _tmp.Combine($"docreg-{Guid.NewGuid():N}.yaml");
        File.WriteAllText(path, YamlText);
        var routes = new RouteCatalog(path);
        return new DocRegistry(new ProtoReflection(), routes);
    }

    [Fact]
    public void Roots_HasTheFourKinds() {
        var roots = Build().Roots();
        var titles = roots.Select(r => r.Title).ToList();
        Assert.Contains("Messages", titles);
        Assert.Contains("Endpoints", titles);
        Assert.Contains("Config", titles);
        Assert.Contains("Controls", titles);
        Assert.Equal(4, roots.Count);
    }

    [Fact]
    public void Messages_IncludeKnownTypeWithFieldChildren() {
        var reg = Build();
        var messages = reg.Roots().Single(r => r.Title == "Messages").Children;

        var contract = messages.SingleOrDefault(m => m.Key == "Contract");
        Assert.NotNull(contract);
        Assert.Equal("message", contract.Kind);
        Assert.NotEmpty(contract.Children);
        Assert.All(contract.Children, c => Assert.Equal("field", c.Kind));
    }

    [Fact]
    public void Endpoints_IncludeKnownRouteWithLinkedTypes() {
        var reg = Build();
        var endpoints = reg.Roots().Single(r => r.Title == "Endpoints").Children;

        var route = endpoints.SingleOrDefault(e => e.Key == "ei/first_contact_secure");
        Assert.NotNull(route);
        Assert.Equal("endpoint", route.Kind);
        Assert.Contains("EggIncFirstContactRequest", route.Summary);
        Assert.Contains("EggIncFirstContactResponse", route.Summary);
        Assert.Contains(route.Children, c => c.Kind == "message" && c.Key == "EggIncFirstContactRequest");
        Assert.Contains(route.Children, c => c.Kind == "message" && c.Key == "EggIncFirstContactResponse");
    }

    [Fact]
    public void Config_IncludesCuratedKeys() {
        var reg = Build();

        var appMode = reg.Find("config", "AppMode");
        Assert.NotNull(appMode);
        Assert.False(string.IsNullOrEmpty(appMode.Summary));

        var rl = reg.Find("config", "RateLimiting:Enabled");
        Assert.NotNull(rl);
        Assert.False(string.IsNullOrEmpty(rl.Summary));
    }

    [Fact]
    public void Controls_ArePresent() {
        var reg = Build();
        var controls = reg.Roots().Single(r => r.Title == "Controls").Children;
        Assert.NotEmpty(controls);
        Assert.All(controls, c => Assert.Equal("control", c.Kind));
    }

    [Fact]
    public void Find_ResolvesMessageAndMisses() {
        var reg = Build();
        Assert.NotNull(reg.Find("message", "Contract"));
        Assert.Null(reg.Find("nope", "nope"));
    }
}
