using System.Text;
using EggIncognito.Services;

namespace EggIncognito.Tests;

public class FileEndpointSourceTests {
    private static string MakeDir(out string root) {
        root = Path.Combine(Path.GetTempPath(), $"ei-fsrc-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "default", "ei"));
        Directory.CreateDirectory(Path.Combine(root, "eids", "EI123", "ei"));
        File.WriteAllText(Path.Combine(root, "default", "ei", "get_periodicals.json"), "{}");
        File.WriteAllText(Path.Combine(root, "eids", "EI123", "ei", "get_periodicals.json"), "{\"a\":1}");
        return root;
    }

    [Fact]
    public void Default_Hit_ReturnsBytes() {
        MakeDir(out string root);
        var src = new FileEndpointSource(root);
        Assert.Equal("{}", Encoding.UTF8.GetString(src.Lookup("ei/get_periodicals", null)!));
    }

    [Fact]
    public void Eid_Beats_Default() {
        MakeDir(out string root);
        var src = new FileEndpointSource(root);
        Assert.Equal("{\"a\":1}", Encoding.UTF8.GetString(src.Lookup("ei/get_periodicals", "EI123")!));
    }

    [Fact]
    public void PathParam_FallsBackToParent() {
        MakeDir(out string root);
        Directory.CreateDirectory(Path.Combine(root, "default", "ei_ctx"));
        File.WriteAllText(Path.Combine(root, "default", "ei_ctx", "get_eval.json"), "{}");
        var src = new FileEndpointSource(root);
        Assert.NotNull(src.Lookup("ei_ctx/get_eval/pumpkin-pie", null));
    }

    [Fact]
    public void Miss_ReturnsNull() {
        MakeDir(out string root);
        Assert.Null(new FileEndpointSource(root).Lookup("ei/nope", null));
    }

    [Fact]
    public void Priority_IsZero() {
        MakeDir(out string root);
        Assert.Equal(0, new FileEndpointSource(root).Priority);
    }
}
