using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Tests;

public class ProtoCleanupTests {
    private const string Ei =
        "syntax = \"proto2\";\n" +
        "\n" +
        "package ei;\n" +
        "\n" +
        "import \"common.proto\";\n" +
        "\n" +
        "message M {\n" +
        "    optional aux.Platform platform = 3;\n" +
        "}\n";

    private const string Common =
        "syntax = \"proto2\";\n" +
        "\n" +
        "package aux;\n" +
        "\n" +
        "enum Platform {\n" +
        "    UNKNOWN_PLATFORM = 0;\n" +
        "    IOS = 1;\n" +
        "}\n";

    [Fact]
    public void Clean_Merges_Common_After_Package_StripsAux_DropsImport() {
        string result = ProtoCleanup.Clean(Ei, Common);

        Assert.DoesNotContain("import \"common.proto\"", result);
        Assert.DoesNotContain("aux.", result);

        int packageIdx = result.IndexOf("package ei;", StringComparison.Ordinal);
        int enumIdx = result.IndexOf("enum Platform {", StringComparison.Ordinal);
        int messageIdx = result.IndexOf("message M {", StringComparison.Ordinal);
        Assert.True(packageIdx >= 0 && enumIdx >= 0 && messageIdx >= 0);

        Assert.True(packageIdx < enumIdx, "enum must follow package ei;");
        Assert.True(enumIdx < messageIdx, "enum (common body) must precede the original message");


        Assert.Contains("Platform platform = 3;", result);

        Assert.Contains("UNKNOWN_PLATFORM = 0;", result);
        Assert.Contains("IOS = 1;", result);
    }

    [Fact]
    public void Clean_Exact_Expected_Output() {
        const string expected =
            "syntax = \"proto2\";\n" +
            "\n" +
            "package ei;\n" +
            "\n" +
            "enum Platform {\n" +
            "    UNKNOWN_PLATFORM = 0;\n" +
            "    IOS = 1;\n" +
            "}" +
            "\n" +
            "\n" +
            "message M {\n" +
            "    optional Platform platform = 3;\n" +
            "}\n";

        Assert.Equal(expected, ProtoCleanup.Clean(Ei, Common));
    }

    [Fact]
    public void Clean_NoCommonBody_LeavesEiMinusImportAndAux() {
        const string ei = "package ei;\nmessage M { optional aux.X x = 1; }\n";
        string result = ProtoCleanup.Clean(ei, "syntax\npackage aux;\n\n");
        Assert.DoesNotContain("aux.", result);
        Assert.Contains("package ei;", result);
    }
}
