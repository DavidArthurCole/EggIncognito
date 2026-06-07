using SeederNS = EggIncognito.Services;

namespace EggIncognito.Tests;

public class RedactorTests
{
    [Theory]
    [InlineData("originalTransactionId")]
    [InlineData("transactionId")]
    [InlineData("linkedTransactionId")]
    [InlineData("deviceId")]
    [InlineData("deviceName")]
    [InlineData("pushUserId")]
    [InlineData("gameServicesId")]
    [InlineData("gameServicesIdScoped")]
    [InlineData("code")]
    [InlineData("coopIdentifier")]
    [InlineData("userName")]
    [InlineData("requestingUserName")]
    [InlineData("alias")]
    public void Redact_JumblesSensitiveField(string field)
    {
        var json = $"{{ \"{field}\": \"super-secret-value-123\" }}";
        var red = SeederNS.Redactor.Redact(json);
        Assert.DoesNotContain("super-secret-value-123", red);
        Assert.Contains($"\"{field}\":", red);          // key preserved
        Assert.Contains("redacted-", red);               // value tokenized
    }

    [Fact]
    public void Redact_IsStable()
    {
        var json = "{ \"deviceId\": \"abc123\" }";
        Assert.Equal(SeederNS.Redactor.Redact(json), SeederNS.Redactor.Redact(json));
    }

    [Fact]
    public void Redact_LeavesNonSensitiveFieldsUntouched()
    {
        var json = "{ \"soulEggs\": \"4659456007327222784\", \"currentEgg\": 14, \"sku\": \"cc_standard\" }";
        Assert.Equal(json, SeederNS.Redactor.Redact(json));
    }

    [Fact]
    public void Redact_RealisticSubscriptionPayload()
    {
        var json = "{ \"originalTransactionId\": \"amdflgjddogdgeejiamdpaej.AO-J1Oy8\", \"periodEnd\": 1782945704.448 }";
        var red = SeederNS.Redactor.Redact(json);
        Assert.DoesNotContain("amdflgjddogdgeejiamdpaej", red);
        Assert.Contains("1782945704.448", red); // non-sensitive numeric kept
    }
}

public class ClassifyAutoWriteTests
{
    private static SeederNS.AutoWriteVerdict Classify(int b, int s) => SeederNS.SeederConfig.ClassifyAutoWrite(b, s);

    [Fact]
    public void NonExactWinner_Rejected() =>
        Assert.Equal(SeederNS.AutoWriteVerdict.Reject, Classify(999, 50));

    [Fact]
    public void SoleExact_Written() =>
        Assert.Equal(SeederNS.AutoWriteVerdict.Write, Classify(1053, 3)); // runner-up not exact

    [Fact]
    public void ExactWithFieldLead_Written()
    {
        // VerifyPurchaseRequest 1053 vs 1003 and ConsumeArtifactRequest 1018 vs 1005 both Write.
        Assert.Equal(SeederNS.AutoWriteVerdict.Write, Classify(1053, 1003));
        Assert.Equal(SeederNS.AutoWriteVerdict.Write, Classify(1018, 1005));
    }

    [Fact]
    public void ExactTie_Flagged() =>
        Assert.Equal(SeederNS.AutoWriteVerdict.Flag, Classify(1010, 1010));
}

public class RoutesYamlEditorTests
{
    // Writes a temp routes.yaml under a fake repo root and returns the root.
    private static string MakeRepo(string yaml)
    {
        var root = Path.Combine(Path.GetTempPath(), $"ei-edit-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "EggIncognito", "RouteMap"));
        File.WriteAllText(Path.Combine(root, "EggIncognito", "RouteMap", "routes.yaml"), yaml);
        return root;
    }

    private static string Read(string root) =>
        File.ReadAllText(Path.Combine(root, "EggIncognito", "RouteMap", "routes.yaml"));

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
        var ed = new SeederNS.RoutesYamlEditor(root);
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
        var ed = new SeederNS.RoutesYamlEditor(root);
        Assert.False(ed.SetFieldIfEmpty("ei/known", "request", "Hacked"));
        ed.Save();
        Assert.Contains("request: KnownRequest", Read(root));
        Assert.DoesNotContain("Hacked", Read(root));
    }

    [Fact]
    public void RemoveFromNeedsCapture_RemovesItem_KeepsHeader()
    {
        var root = MakeRepo(Sample);
        var ed = new SeederNS.RoutesYamlEditor(root);
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
        var ed = new SeederNS.RoutesYamlEditor(root);
        Assert.True(ed.RequestUnresolved("ei/unknown"));
        Assert.True(ed.MarkRequestNone("ei/unknown"));
        Assert.False(ed.RequestUnresolved("ei/unknown")); // now resolved
        ed.Save();
        var once = Read(root);
        // Re-applying yields identical output (stable), even if the call reports a write.
        var ed2 = new SeederNS.RoutesYamlEditor(root);
        ed2.MarkRequestNone("ei/unknown");
        ed2.Save();
        Assert.Equal(once, Read(root));
    }

    [Fact]
    public void AddEndpoint_LandsInSection_AndParses()
    {
        var root = MakeRepo(Sample);
        var ed = new SeederNS.RoutesYamlEditor(root);
        Assert.True(ed.AddRoute("ei/brand_new", "NewReq", false, "NewResp", true));
        ed.Save();
        var yaml = Read(root);
        Assert.Contains("- path: ei/brand_new", yaml);
        Assert.Contains("response: NewResp", yaml);
        // The new block must sit inside routes:, before needs_capture:.
        Assert.True(yaml.IndexOf("ei/brand_new") < yaml.IndexOf("needs_capture:"));
    }
}
