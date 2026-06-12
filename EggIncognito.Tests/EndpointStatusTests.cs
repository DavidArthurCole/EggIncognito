using EggIncognito.Services;

namespace EggIncognito.Tests;

public class EndpointStatusTests
{
    private static string WriteYaml(string dir)
    {
        var p = Path.Combine(dir, "routes.yaml");
        File.WriteAllText(p, """
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
            """);
        return p;
    }

    private static (string yamlPath, string defaults) MakeRepo()
    {
        var dir = Path.Combine(Path.GetTempPath(), "egi-st-" + Guid.NewGuid().ToString("N"));
        var defaults = Path.Combine(dir, "default");
        Directory.CreateDirectory(Path.Combine(defaults, "ei"));
        var yamlPath = WriteYaml(dir);
        File.WriteAllText(Path.Combine(defaults, "ei", "has_endpoint.json"), "{ \"x\": 1 }");
        File.WriteAllText(Path.Combine(defaults, "ei", "empty_endpoint.json"), "{}");
        return (yamlPath, defaults);
    }

    [Fact]
    public void Classify_BucketsMissingEmptyOk_SkippingRaw()
    {
        var (yamlPath, defaults) = MakeRepo();
        var r = EndpointStatus.Classify(yamlPath, defaults);
        Assert.Contains("ei/has_endpoint", r.Ok);
        Assert.Contains("ei/empty_endpoint", r.Empty);
        Assert.Contains("ei/missing_endpoint", r.Missing);
        Assert.DoesNotContain("ei/raw_one", r.Ok);
        Assert.DoesNotContain("ei/raw_one", r.Empty);
        Assert.DoesNotContain("ei/raw_one", r.Missing);
    }

    [Fact]
    public void WriteStatusBlock_RewritesEndpointStatus()
    {
        var (yamlPath, defaults) = MakeRepo();
        var yaml = EndpointStatus.WriteStatusBlock(yamlPath, EndpointStatus.Classify(yamlPath, defaults));
        Assert.Contains("endpoint_status:", yaml);
        Assert.Contains("ei/missing_endpoint", yaml);
        Assert.Contains("ei/empty_endpoint", yaml);
    }

    [Theory]
    [InlineData("{\n}")]
    [InlineData("{  }")]
    [InlineData("{\r\n}")]
    [InlineData("  ")]
    public void Classify_WhitespaceVariantsOfEmptyObject_AreEmpty(string content)
    {
        var (yamlPath, defaults) = MakeRepo();
        File.WriteAllText(Path.Combine(defaults, "ei", "empty_endpoint.json"), content);
        var r = EndpointStatus.Classify(yamlPath, defaults);
        Assert.Contains("ei/empty_endpoint", r.Empty);
        Assert.DoesNotContain("ei/empty_endpoint", r.Ok);
    }

    [Fact]
    public void WriteStatusBlock_PreservesFollowingNonLowercaseTopLevelKey()
    {
        var (yamlPath, defaults) = MakeRepo();
        // An existing block followed by top-level keys the old lowercase-only lookahead would eat.
        File.AppendAllText(yamlPath,
            "\nendpoint_status:\n  missing:\n    - ei/stale_entry\n\n_meta: keep_underscore\nZone: keep_upper\n");
        var yaml = EndpointStatus.WriteStatusBlock(yamlPath, EndpointStatus.Classify(yamlPath, defaults));
        Assert.Contains("_meta: keep_underscore", yaml);
        Assert.Contains("Zone: keep_upper", yaml);
        Assert.DoesNotContain("ei/stale_entry", yaml); // old block fully replaced
        Assert.Contains("ei/missing_endpoint", yaml);
    }
}
