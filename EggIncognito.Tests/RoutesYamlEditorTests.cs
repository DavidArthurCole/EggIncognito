using Svc = EggIncognito.Services;

namespace EggIncognito.Tests;

public class RoutesYamlEditorTests
{
    private static string MakeRepo(string yaml) => TestRepoFixture.MakeRepo(yaml, "ei-edit", withSlnxMarker: false);

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

    // Non-standard indentation: items at 4 spaces, fields at 6.
    private const string DeepIndent = """
routes:
    - path: ei/deep
      request:
      response: KnownResponse

    - path: ei/bare
""";

    [Fact]
    public void SetWrappedFlag_InsertMatchesSiblingFieldIndent()
    {
        var root = MakeRepo(DeepIndent);
        var ed = new Svc.RoutesYamlEditor(root);
        Assert.True(ed.SetWrappedFlag("ei/deep", "responseWrapped"));
        ed.Save();
        Assert.Contains("\n      responseWrapped: true", Read(root));
    }

    [Fact]
    public void SetFieldIfEmpty_InsertDerivesIndentFromPathLine()
    {
        var root = MakeRepo(DeepIndent);
        var ed = new Svc.RoutesYamlEditor(root);
        Assert.True(ed.SetFieldIfEmpty("ei/bare", "request", "FoundRequest"));
        ed.Save();
        Assert.Contains("\n      request: FoundRequest", Read(root));
    }

    [Fact]
    public void MarkRequestNone_InsertDerivesIndentFromPathLine()
    {
        var root = MakeRepo(DeepIndent);
        var ed = new Svc.RoutesYamlEditor(root);
        Assert.True(ed.MarkRequestNone("ei/bare"));
        ed.Save();
        Assert.Contains("\n      request:  # none - empty body", Read(root));
    }

    [Fact]
    public void Save_AbortsWhenFileChangedSinceLoad()
    {
        var root = MakeRepo(Sample);
        var ed = new Svc.RoutesYamlEditor(root);
        Assert.True(ed.SetFieldIfEmpty("ei/unknown", "request", "FoundRequest"));

        // A concurrent writer edits the file after our load.
        var p = Path.Combine(root, "RouteMap", "routes.yaml");
        File.WriteAllText(p, Sample + "\n# concurrent edit\n");
        File.SetLastWriteTimeUtc(p, DateTime.UtcNow.AddSeconds(5));

        Assert.Throws<IOException>(() => ed.Save());
        Assert.Contains("# concurrent edit", Read(root)); // their edit survives
        Assert.DoesNotContain("FoundRequest", Read(root)); // our stale view not flushed
    }

    [Fact]
    public void Save_RefreshesStamp_SoSecondSaveWorks()
    {
        var root = MakeRepo(Sample);
        var ed = new Svc.RoutesYamlEditor(root);
        Assert.True(ed.SetFieldIfEmpty("ei/unknown", "request", "FoundRequest"));
        ed.Save();
        Assert.True(ed.RemoveFromNeedsCapture("ei/unknown"));
        ed.Save(); // must not throw: the first Save refreshed the stamp
        Assert.DoesNotContain("- ei/unknown", Read(root));
    }
}
