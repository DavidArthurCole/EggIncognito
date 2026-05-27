using System.Collections.Generic;
using System.Text;

namespace EggIncognito.CodeGen.Generators;

public sealed class RubyGenerator : IServerGenerator
{
    public string Language => "Ruby";

    private static string BuildRoutes(IReadOnlyList<EndpointEntry> endpoints) =>
        string.Join("\n", endpoints.Select(ep =>
            $"post '/{ep.Path}' do serve('{ep.Slug}') end"));

    public void Generate(IReadOnlyList<EndpointEntry> endpoints, string fixturesPath, string outputDir, int port)
    {
        var subs = new Dictionary<string, string>
        {
            ["PORT"] = port.ToString(),
            ["ROUTES"] = BuildRoutes(endpoints),
        };

        File.WriteAllText(Path.Combine(outputDir, "server.rb"),
            TemplateLoader.Load("ruby", "server.rb", subs), new UTF8Encoding(false));

        File.WriteAllText(Path.Combine(outputDir, "Gemfile"),
            TemplateLoader.Load("ruby", "Gemfile"), new UTF8Encoding(false));

        GoGenerator.WriteReadme(outputDir, port,
            run: "bundle install\nruby server.rb",
            prereqs: "Ruby 3.1+, Bundler");
    }

}
