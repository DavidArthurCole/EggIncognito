using System.Text.Json;
using EggIncognito.Services;

namespace EggIncognito.Tests;

public class PostmanCollectionTests {
    [Fact]
    public void BuildJson_ProducesNamespaceFoldersAndSimulation() {
        var dir = Path.Combine(Path.GetTempPath(), "egi-pm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "routes.yaml"), """
            routes:
              - path: ei/alpha
                request: EggIncFirstContactRequest
                response: EggIncFirstContactResponse
              - path: ei_afx/beta
                request: EggIncFirstContactRequest
                response: EggIncFirstContactResponse
            """);

        using var doc = JsonDocument.Parse(PostmanCollection.BuildJson(Path.Combine(dir, "routes.yaml")));
        var root = doc.RootElement;
        Assert.Equal("EggIncognito", root.GetProperty("info").GetProperty("name").GetString());
        var names = root.GetProperty("item").EnumerateArray()
            .Select(i => i.GetProperty("name").GetString()).ToList();
        Assert.Contains("ei", names);
        Assert.Contains("ei_afx", names);
        Assert.Contains("Simulation", names);
    }
}
