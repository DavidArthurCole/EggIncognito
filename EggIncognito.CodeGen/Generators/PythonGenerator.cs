using System.Collections.Generic;
using System.Text;

namespace EggIncognito.CodeGen.Generators;

public sealed class PythonGenerator : IServerGenerator
{
    public string Language => "Python";

    private static string BuildRoutes(IReadOnlyList<EndpointEntry> endpoints) =>
        string.Join("\n\n", endpoints.Select(ep =>
            $"@app.post(\"/{ep.Path}\")\ndef handle_{ep.Slug.Replace('/', '_')}():\n    return _serve(\"{ep.Slug}\")"));

    public void Generate(IReadOnlyList<EndpointEntry> endpoints, string fixturesPath, string outputDir, int port)
    {
        var subs = new Dictionary<string, string>
        {
            ["PORT"] = port.ToString(),
            ["ROUTES"] = BuildRoutes(endpoints),
        };

        File.WriteAllText(Path.Combine(outputDir, "server.py"),
            TemplateLoader.Load("python", "server.py", subs), new UTF8Encoding(false));

        File.WriteAllText(Path.Combine(outputDir, "requirements.txt"),
            TemplateLoader.Load("python", "requirements.txt"), new UTF8Encoding(false));

        GoGenerator.WriteReadme(outputDir, port,
            run: "pip install -r requirements.txt\npython server.py",
            prereqs: "Python 3.11+");
    }

}
