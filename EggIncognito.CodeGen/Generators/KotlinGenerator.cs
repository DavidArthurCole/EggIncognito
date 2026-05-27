using System.Collections.Generic;
using System.Text;

namespace EggIncognito.CodeGen.Generators;

public sealed class KotlinGenerator : IServerGenerator
{
    public string Language => "Kotlin";

    private static string BuildRoutes(IReadOnlyList<EndpointEntry> endpoints) =>
        string.Join("\n", endpoints.Select(ep =>
            $"    post(\"/{ep.Path}\") {{ serve(call, \"{ep.Slug}\") }}"));

    public void Generate(IReadOnlyList<EndpointEntry> endpoints, string fixturesPath, string outputDir, int port)
    {
        var srcDir = Path.Combine(outputDir, "src", "main", "kotlin");
        Directory.CreateDirectory(srcDir);

        var subs = new Dictionary<string, string>
        {
            ["PORT"] = port.ToString(),
            ["ROUTES"] = BuildRoutes(endpoints),
        };

        File.WriteAllText(Path.Combine(srcDir, "Server.kt"),
            TemplateLoader.Load("kotlin", "Server.kt", subs), new UTF8Encoding(false));

        File.WriteAllText(Path.Combine(outputDir, "build.gradle.kts"),
            TemplateLoader.Load("kotlin", "build.gradle.kts"), new UTF8Encoding(false));

        GoGenerator.WriteReadme(outputDir, port,
            run: "./gradlew run",
            prereqs: "Kotlin 2.0+, JDK 17+, Gradle 8+");
    }

}
