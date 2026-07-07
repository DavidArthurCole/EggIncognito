namespace EggIncognito.Tests;

internal static class TestRepoFixture
{
    internal static string MakeRepo(string yaml, string prefix, bool withSlnxMarker = true)
    {
        var root = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "RouteMap"));
        if (withSlnxMarker)
            File.WriteAllText(Path.Combine(root, "EggIncognito.slnx"), "<Solution />");
        File.WriteAllText(Path.Combine(root, "RouteMap", "routes.yaml"), yaml);
        return root;
    }
}
