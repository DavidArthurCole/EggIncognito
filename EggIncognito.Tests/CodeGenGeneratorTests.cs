extern alias CodeGen;
using CodeGen::EggIncognito.CodeGen;
using CodeGen::EggIncognito.CodeGen.Generators;

namespace EggIncognito.Tests;

public sealed class CodeGenGeneratorTests : IDisposable
{
    private readonly string _outputDir;
    private static readonly IReadOnlyList<EndpointEntry> _endpoints =
    [
        new EndpointEntry("ei/test_endpoint", "AuthenticatedMessage", "AuthenticatedMessage"),
    ];

    public CodeGenGeneratorTests()
    {
        _outputDir = Path.Combine(Path.GetTempPath(), $"ei-gen-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_outputDir);
    }

    public void Dispose() => Directory.Delete(_outputDir, recursive: true);

    [Fact]
    public void GoGenerator_ProducesExpectedFiles()
    {
        new GoGenerator().Generate(_endpoints, "Fixtures", _outputDir, 5080);

        Assert.True(File.Exists(Path.Combine(_outputDir, "server.go")));
        Assert.True(File.Exists(Path.Combine(_outputDir, "go.mod")));
        Assert.True(File.Exists(Path.Combine(_outputDir, "README.md")));
    }

    [Fact]
    public void GoGenerator_SubstitutesPort()
    {
        new GoGenerator().Generate(_endpoints, "Fixtures", _outputDir, 9999);
        var content = File.ReadAllText(Path.Combine(_outputDir, "server.go"));
        Assert.Contains("9999", content);
        Assert.DoesNotContain("{PORT}", content);
    }

    [Fact]
    public void GoGenerator_EmitsRoutes()
    {
        new GoGenerator().Generate(_endpoints, "Fixtures", _outputDir, 5080);
        var content = File.ReadAllText(Path.Combine(_outputDir, "server.go"));
        Assert.Contains("ei/test_endpoint", content);
        Assert.Contains("ei_test_endpoint", content);
    }

    [Fact]
    public void PythonGenerator_ProducesExpectedFiles()
    {
        new PythonGenerator().Generate(_endpoints, "Fixtures", _outputDir, 5080);
        Assert.True(File.Exists(Path.Combine(_outputDir, "server.py")));
        Assert.True(File.Exists(Path.Combine(_outputDir, "requirements.txt")));
        Assert.True(File.Exists(Path.Combine(_outputDir, "README.md")));
    }

    [Fact]
    public void JavaScriptGenerator_ProducesExpectedFiles()
    {
        new JavaScriptGenerator().Generate(_endpoints, "Fixtures", _outputDir, 5080);
        Assert.True(File.Exists(Path.Combine(_outputDir, "server.js")));
        Assert.True(File.Exists(Path.Combine(_outputDir, "package.json")));
        Assert.True(File.Exists(Path.Combine(_outputDir, "README.md")));
    }

    [Fact]
    public void JavaGenerator_ProducesExpectedFiles()
    {
        new JavaGenerator().Generate(_endpoints, "Fixtures", _outputDir, 5080);
        Assert.True(File.Exists(Path.Combine(_outputDir, "build.gradle.kts")));
        Assert.True(File.Exists(Path.Combine(_outputDir, "settings.gradle.kts")));
        Assert.True(File.Exists(Path.Combine(_outputDir, "README.md")));
        var srcDir = Path.Combine(_outputDir, "src", "main", "java", "com", "egginc", "mock");
        Assert.True(File.Exists(Path.Combine(srcDir, "Server.java")));
    }

    [Fact]
    public void KotlinGenerator_ProducesExpectedFiles()
    {
        new KotlinGenerator().Generate(_endpoints, "Fixtures", _outputDir, 5080);
        Assert.True(File.Exists(Path.Combine(_outputDir, "build.gradle.kts")));
        Assert.True(File.Exists(Path.Combine(_outputDir, "README.md")));
        var srcDir = Path.Combine(_outputDir, "src", "main", "kotlin");
        Assert.True(File.Exists(Path.Combine(srcDir, "Server.kt")));
    }

    [Fact]
    public void RubyGenerator_ProducesExpectedFiles()
    {
        new RubyGenerator().Generate(_endpoints, "Fixtures", _outputDir, 5080);
        Assert.True(File.Exists(Path.Combine(_outputDir, "server.rb")));
        Assert.True(File.Exists(Path.Combine(_outputDir, "Gemfile")));
        Assert.True(File.Exists(Path.Combine(_outputDir, "README.md")));
    }

    [Fact]
    public void TemplateLoader_ThrowsForUnknownTemplate()
    {
        Assert.Throws<InvalidOperationException>(() =>
            TemplateLoader.Load("go", "nonexistent.go"));
    }

    [Fact]
    public void ReadmeContainsCorrectLanguageName()
    {
        new GoGenerator().Generate(_endpoints, "Fixtures", _outputDir, 5080);
        var readme = File.ReadAllText(Path.Combine(_outputDir, "README.md"));
        Assert.Contains("Go", readme);
        Assert.DoesNotContain("{LANGUAGE}", readme);
        Assert.DoesNotContain("{PORT}", readme);
        Assert.DoesNotContain("{SLUG}", readme);
    }
}
