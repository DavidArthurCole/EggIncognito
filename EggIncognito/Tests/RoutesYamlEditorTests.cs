using Svc = EggIncognito.Services;

namespace EggIncognito.Tests;

public class RoutesYamlEditorTests {
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


    private const string DeepIndent = """
                                      routes:
                                          - path: ei/deep
                                            request:
                                            response: KnownResponse

                                          - path: ei/bare
                                      """;

    private static string MakeRepo(string yaml) => TestRepoFixture.MakeRepo(yaml, "ei-edit", false);

    private static string Read(string root) =>
        File.ReadAllText(Path.Combine(root, "RouteMap", "routes.yaml"));

    [Fact]
    public void SetFieldIfEmpty_FillsPlaceholder_StripsComment() {
        string root = MakeRepo(Sample);
        var ed = new Svc.RoutesYamlEditor(root);
        Assert.True(ed.SetFieldIfEmpty("ei/unknown", "request", "FoundRequest"));
        ed.Save();
        string yaml = Read(root);
        Assert.Contains("request: FoundRequest", yaml);
        Assert.DoesNotContain("NEEDS CAPTURE", yaml);
    }

    [Fact]
    public void SetFieldIfEmpty_NeverClobbersConcrete() {
        string root = MakeRepo(Sample);
        var ed = new Svc.RoutesYamlEditor(root);
        Assert.False(ed.SetFieldIfEmpty("ei/known", "request", "Hacked"));
        ed.Save();
        Assert.Contains("request: KnownRequest", Read(root));
        Assert.DoesNotContain("Hacked", Read(root));
    }

    [Fact]
    public void RemoveFromNeedsCapture_RemovesItem_KeepsHeader() {
        string root = MakeRepo(Sample);
        var ed = new Svc.RoutesYamlEditor(root);
        Assert.True(ed.RemoveFromNeedsCapture("ei/unknown"));
        ed.Save();
        string yaml = Read(root);
        Assert.DoesNotContain("- ei/unknown", yaml);
        Assert.Contains("request_unknown:", yaml);
    }

    [Fact]
    public void MarkRequestNone_IsResolvedAndStable() {
        string root = MakeRepo(Sample);
        var ed = new Svc.RoutesYamlEditor(root);
        Assert.True(ed.RequestUnresolved("ei/unknown"));
        Assert.True(ed.MarkRequestNone("ei/unknown"));
        Assert.False(ed.RequestUnresolved("ei/unknown"));
        ed.Save();
        string once = Read(root);

        var ed2 = new Svc.RoutesYamlEditor(root);
        ed2.MarkRequestNone("ei/unknown");
        ed2.Save();
        Assert.Equal(once, Read(root));
    }

    [Fact]
    public void AddEndpoint_LandsInSection_AndParses() {
        string root = MakeRepo(Sample);
        var ed = new Svc.RoutesYamlEditor(root);
        Assert.True(ed.AddRoute("ei/brand_new", "NewReq", false, "NewResp", true));
        ed.Save();
        string yaml = Read(root);
        Assert.Contains("- path: ei/brand_new", yaml);
        Assert.Contains("response: NewResp", yaml);

        Assert.True(yaml.IndexOf("ei/brand_new") < yaml.IndexOf("needs_capture:"));
    }

    [Fact]
    public void SetWrappedFlag_InsertMatchesSiblingFieldIndent() {
        string root = MakeRepo(DeepIndent);
        var ed = new Svc.RoutesYamlEditor(root);
        Assert.True(ed.SetWrappedFlag("ei/deep", "responseWrapped"));
        ed.Save();
        Assert.Contains("\n      responseWrapped: true", Read(root));
    }

    [Fact]
    public void SetFieldIfEmpty_InsertDerivesIndentFromPathLine() {
        string root = MakeRepo(DeepIndent);
        var ed = new Svc.RoutesYamlEditor(root);
        Assert.True(ed.SetFieldIfEmpty("ei/bare", "request", "FoundRequest"));
        ed.Save();
        Assert.Contains("\n      request: FoundRequest", Read(root));
    }

    [Fact]
    public void MarkRequestNone_InsertDerivesIndentFromPathLine() {
        string root = MakeRepo(DeepIndent);
        var ed = new Svc.RoutesYamlEditor(root);
        Assert.True(ed.MarkRequestNone("ei/bare"));
        ed.Save();
        Assert.Contains("\n      request:  # none - empty body", Read(root));
    }

    [Fact]
    public void Save_AbortsWhenFileChangedSinceLoad() {
        string root = MakeRepo(Sample);
        var ed = new Svc.RoutesYamlEditor(root);
        Assert.True(ed.SetFieldIfEmpty("ei/unknown", "request", "FoundRequest"));


        string p = Path.Combine(root, "RouteMap", "routes.yaml");
        File.WriteAllText(p, Sample + "\n# concurrent edit\n");
        File.SetLastWriteTimeUtc(p, DateTime.UtcNow.AddSeconds(5));

        Assert.Throws<IOException>(() => ed.Save());
        Assert.Contains("# concurrent edit", Read(root));
        Assert.DoesNotContain("FoundRequest", Read(root));
    }

    [Fact]
    public void Save_RefreshesStamp_SoSecondSaveWorks() {
        string root = MakeRepo(Sample);
        var ed = new Svc.RoutesYamlEditor(root);
        Assert.True(ed.SetFieldIfEmpty("ei/unknown", "request", "FoundRequest"));
        ed.Save();
        Assert.True(ed.RemoveFromNeedsCapture("ei/unknown"));
        ed.Save();
        Assert.DoesNotContain("- ei/unknown", Read(root));
    }
}
