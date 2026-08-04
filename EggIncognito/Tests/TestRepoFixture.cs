namespace EggIncognito.Tests;

internal static class TestRepoFixture {
    internal static string MakeRepo(TempDir tmp, string yaml, bool withSlnxMarker = true) {
        string root = tmp.CreateSubdir();
        Directory.CreateDirectory(Path.Combine(root, "RouteMap"));
        if (withSlnxMarker)
            File.WriteAllText(Path.Combine(root, "EggIncognito.slnx"), "<Solution />");
        File.WriteAllText(Path.Combine(root, "RouteMap", "routes.yaml"), yaml);
        return root;
    }
}
