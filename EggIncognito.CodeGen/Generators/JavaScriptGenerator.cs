using System.Collections.Generic;
using System.Text;

namespace EggIncognito.CodeGen.Generators;

public sealed class JavaScriptGenerator : IServerGenerator
{
    public string Language => "JavaScript";

    private static string BuildRoutes(IReadOnlyList<EndpointEntry> endpoints) =>
        string.Join("\n", endpoints.Select(ep =>
            $"app.post('/{ep.Path}', makeHandler('{ep.Slug}'));"));

    public void Generate(IReadOnlyList<EndpointEntry> endpoints, string fixturesPath, string outputDir, int port)
    {
        var subs = new Dictionary<string, string>
        {
            ["PORT"] = port.ToString(),
            ["ROUTES"] = BuildRoutes(endpoints),
        };

        File.WriteAllText(Path.Combine(outputDir, "server.js"),
            TemplateLoader.Load("javascript", "server.js", subs), new UTF8Encoding(false));

        File.WriteAllText(Path.Combine(outputDir, "package.json"),
            TemplateLoader.Load("javascript", "package.json"), new UTF8Encoding(false));

        GoGenerator.WriteReadme(outputDir, port,
            run: "npm install\nnode server.js",
            prereqs: "Node.js 18+");
    }

}
