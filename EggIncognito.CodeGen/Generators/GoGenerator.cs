using System.Collections.Generic;
using System.Text;

namespace EggIncognito.CodeGen.Generators;

public sealed class GoGenerator : IServerGenerator
{
    public string Language => "Go";

    private static string BuildRoutes(IReadOnlyList<EndpointEntry> endpoints) =>
        string.Join("\n", endpoints.Select(ep =>
            $"\tmux.HandleFunc(\"/{ep.Path}\", makeHandler(\"{ep.Slug}\"))"));

    public void Generate(IReadOnlyList<EndpointEntry> endpoints, string fixturesPath, string outputDir, int port)
    {
        var subs = new Dictionary<string, string>
        {
            ["PORT"] = port.ToString(),
            ["ROUTES"] = BuildRoutes(endpoints),
        };

        File.WriteAllText(Path.Combine(outputDir, "server.go"),
            TemplateLoader.Load("go", "server.go", subs), new UTF8Encoding(false));

        File.WriteAllText(Path.Combine(outputDir, "go.mod"),
            TemplateLoader.Load("go", "go.mod"), new UTF8Encoding(false));

        WriteReadme(outputDir, port,
            run: "go run .",
            prereqs: "Go 1.21+");
    }

    private static readonly Dictionary<string, string> _displayNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["go"] = "Go", ["python"] = "Python", ["javascript"] = "JavaScript",
        ["java"] = "Java", ["kotlin"] = "Kotlin", ["ruby"] = "Ruby", ["csharp"] = "C#",
    };

    internal static void WriteReadme(string outputDir, int port, string run, string prereqs)
    {
        var slug = Path.GetFileName(outputDir);
        var subs = new Dictionary<string, string>
        {
            ["LANGUAGE"] = _displayNames.GetValueOrDefault(slug, slug),
            ["SLUG"] = slug,
            ["PORT"] = port.ToString(),
            ["RUN"] = run,
            ["PREREQS"] = prereqs,
        };
        File.WriteAllText(Path.Combine(outputDir, "README.md"),
            TemplateLoader.Load("shared", "README.md", subs), new UTF8Encoding(false));
    }
}
