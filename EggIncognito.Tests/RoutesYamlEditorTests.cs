using Svc = EggIncognito.Services;

namespace EggIncognito.Tests;

public class RoutesYamlEditorTests
{
    // Writes a temp routes.yaml under a fake repo root and returns the root.
    private static string MakeRepo(string yaml)
    {
        var root = Path.Combine(Path.GetTempPath(), $"ei-edit-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "RouteMap"));
        File.WriteAllText(Path.Combine(root, "RouteMap", "routes.yaml"), yaml);
        return root;
    }

    private static string Read(string root) =>
        File.ReadAllText(Path.Combine(root, "RouteMap", "routes.yaml"));

    private const string Sample = """
routes:
  # ei/
  - path: ei/known
    request: KnownRequest
    response: KnownResponse
  - path: ei/unknown
    request:  # NEEDS CAPTURE - signed request, inner type unknown
    requestWrapped: true
    response: SomeResponse
    responseWrapped: true

needs_capture:
  request_unknown:
    - ei/unknown
""";

    [Fact]
    public void SetFieldIfEmpty_FillsPlaceholder_StripsComment()
    {
        var root = MakeRepo(Sample);
        var ed = new Svc.RoutesYamlEditor(root);
        Assert.True(ed.SetFieldIfEmpty("ei/unknown", "request", "FoundRequest"));
        ed.Save();
        var yaml = Read(root);
        Assert.Contains("request: FoundRequest", yaml);
        Assert.DoesNotContain("NEEDS CAPTURE", yaml);
    }

    [Fact]
    public void SetFieldIfEmpty_NeverClobbersConcrete()
    {
        var root = MakeRepo(Sample);
        var ed = new Svc.RoutesYamlEditor(root);
        Assert.False(ed.SetFieldIfEmpty("ei/known", "request", "Hacked"));
        ed.Save();
        Assert.Contains("request: KnownRequest", Read(root));
        Assert.DoesNotContain("Hacked", Read(root));
    }

    [Fact]
    public void RemoveFromNeedsCapture_RemovesItem_KeepsHeader()
    {
        var root = MakeRepo(Sample);
        var ed = new Svc.RoutesYamlEditor(root);
        Assert.True(ed.RemoveFromNeedsCapture("ei/unknown"));
        ed.Save();
        var yaml = Read(root);
        Assert.DoesNotContain("- ei/unknown", yaml);
        Assert.Contains("request_unknown:", yaml); // header kept
    }

    [Fact]
    public void MarkRequestNone_IsResolvedAndStable()
    {
        var root = MakeRepo(Sample);
        var ed = new Svc.RoutesYamlEditor(root);
        Assert.True(ed.RequestUnresolved("ei/unknown"));
        Assert.True(ed.MarkRequestNone("ei/unknown"));
        Assert.False(ed.RequestUnresolved("ei/unknown")); // now resolved
        ed.Save();
        var once = Read(root);
        // Re-applying yields identical output (stable), even if the call reports a write.
        var ed2 = new Svc.RoutesYamlEditor(root);
        ed2.MarkRequestNone("ei/unknown");
        ed2.Save();
        Assert.Equal(once, Read(root));
    }

    [Fact]
    public void AddEndpoint_LandsInSection_AndParses()
    {
        var root = MakeRepo(Sample);
        var ed = new Svc.RoutesYamlEditor(root);
        Assert.True(ed.AddRoute("ei/brand_new", "NewReq", false, "NewResp", true));
        ed.Save();
        var yaml = Read(root);
        Assert.Contains("- path: ei/brand_new", yaml);
        Assert.Contains("response: NewResp", yaml);
        // The new block must sit inside routes:, before needs_capture:.
        Assert.True(yaml.IndexOf("ei/brand_new") < yaml.IndexOf("needs_capture:"));
    }
}
