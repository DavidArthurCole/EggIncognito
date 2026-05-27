using System.Collections.Generic;
using System.Text;

namespace EggIncognito.CodeGen.Generators;

public sealed class JavaGenerator : IServerGenerator
{
    public string Language => "Java";

    private static string BuildRoutes(IReadOnlyList<EndpointEntry> endpoints) =>
        string.Join("\n", endpoints.Select(ep =>
            $"        app.post(\"/{ep.Path}\", ctx -> serve(ctx, \"{ep.Slug}\"));"));

    public void Generate(IReadOnlyList<EndpointEntry> endpoints, string fixturesPath, string outputDir, int port)
    {
        var srcDir = Path.Combine(outputDir, "src", "main", "java", "com", "egginc", "mock");
        Directory.CreateDirectory(srcDir);

        var subs = new Dictionary<string, string>
        {
            ["PORT"] = port.ToString(),
            ["ROUTES"] = BuildRoutes(endpoints),
        };

        File.WriteAllText(Path.Combine(srcDir, "Server.java"),
            TemplateLoader.Load("java", "Server.java", subs), new UTF8Encoding(false));

        File.WriteAllText(Path.Combine(outputDir, "build.gradle.kts"),
            TemplateLoader.Load("java", "build.gradle.kts"), new UTF8Encoding(false));

        File.WriteAllText(Path.Combine(outputDir, "settings.gradle.kts"),
            TemplateLoader.Load("java", "settings.gradle.kts"), new UTF8Encoding(false));

        GoGenerator.WriteReadme(outputDir, port,
            run: "gradle shadowJar\njava -jar build/libs/*-all.jar",
            prereqs: "Java 17+, Gradle 8+");
    }

}
