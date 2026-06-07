using EggIncognito.Cli;

namespace EggIncognito.Tests;

public class CheckEndpointsCommandTests
{
    private static string WriteYaml(string dir)
    {
        var yaml = """
            routes:
              - path: ei/has_endpoint
                request: A
                response: B
              - path: ei/empty_endpoint
                request: A
                response: B
              - path: ei/missing_endpoint
                request: A
                response: B
              - path: ei/raw_one
                request: A
                rawResponse: "OK"
            """;
        var p = Path.Combine(dir, "routes.yaml");
        File.WriteAllText(p, yaml);
        return p;
    }

    [Fact]
    public void Classify_BucketsMissingEmptyOk_SkippingRaw()
    {
        var dir = Path.Combine(Path.GetTempPath(), "egi-chk-" + Guid.NewGuid().ToString("N"));
        var defaults = Path.Combine(dir, "default");
        Directory.CreateDirectory(Path.Combine(defaults, "ei"));
        var yamlPath = WriteYaml(dir);
        File.WriteAllText(Path.Combine(defaults, "ei", "has_endpoint.json"), "{ \"x\": 1 }");
        File.WriteAllText(Path.Combine(defaults, "ei", "empty_endpoint.json"), "{}");
        // missing_endpoint.json intentionally absent

        var result = CheckEndpointsCommand.Classify(yamlPath, defaults);

        Assert.Contains("ei/has_endpoint", result.Ok);
        Assert.Contains("ei/empty_endpoint", result.Empty);
        Assert.Contains("ei/missing_endpoint", result.Missing);
        Assert.DoesNotContain("ei/raw_one", result.Ok);
        Assert.DoesNotContain("ei/raw_one", result.Empty);
        Assert.DoesNotContain("ei/raw_one", result.Missing);
    }
}
