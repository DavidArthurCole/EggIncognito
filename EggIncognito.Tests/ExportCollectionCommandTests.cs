using System.Text.Json;
using EggIncognito.Cli;

namespace EggIncognito.Tests;

public class ExportCollectionCommandTests
{
    [Fact]
    public void Build_ProducesValidPostmanCollection_WithNamespaceFolders()
    {
        var dir = Path.Combine(Path.GetTempPath(), "egi-exp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var yaml = """
            routes:
              - path: ei/alpha
                request: EggIncFirstContactRequest
                response: EggIncFirstContactResponse
              - path: ei_afx/beta
                request: EggIncFirstContactRequest
                response: EggIncFirstContactResponse
            """;
        var yamlPath = Path.Combine(dir, "routes.yaml");
        File.WriteAllText(yamlPath, yaml);

        var json = ExportCollectionCommand.BuildJson(yamlPath);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("EggIncognito", root.GetProperty("info").GetProperty("name").GetString());
        var items = root.GetProperty("item");
        Assert.True(items.GetArrayLength() >= 3); // ei + ei_afx + Simulation
        var names = items.EnumerateArray().Select(i => i.GetProperty("name").GetString()).ToList();
        Assert.Contains("ei", names);
        Assert.Contains("ei_afx", names);
        Assert.Contains("Simulation", names);
    }
}
