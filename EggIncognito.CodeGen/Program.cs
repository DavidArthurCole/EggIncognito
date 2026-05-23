using EggIncognito.CodeGen;
using EggIncognito.CodeGen.Baking;
using EggIncognito.CodeGen.Generators;

var repoRoot = EndpointLoader.FindRepoRoot();
var defaultYaml = Path.Combine(repoRoot, "EggIncognito", "EndpointMap", "endpoints.yaml");
var defaultFix = Path.Combine(repoRoot, "EggIncognito", "Fixtures");
var defaultOut = Path.Combine(repoRoot, "generated");

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    PrintHelp();
    return 0;
}

var command = args[0].ToLowerInvariant();
var flags = ParseFlags(args.Skip(1).ToArray());

switch (command)
{
    case "bake":
    {
        var yamlPath = flags.GetValueOrDefault("--yaml", defaultYaml);
        var fixturesPath = flags.GetValueOrDefault("--fixtures", defaultFix);
        var endpoints = EndpointLoader.Load(yamlPath);
        var typeMap = EndpointTypeMap.Build(endpoints);
        Console.WriteLine($"Baking fixtures in: {fixturesPath}");
        var count = FixtureBaker.Bake(fixturesPath, typeMap);
        Console.WriteLine($"Baked {count} fixture(s).");
        return 0;
    }

    case "generate":
        return await RunGenerate(args, flags, defaultYaml, defaultFix, defaultOut);

    default:
        Console.Error.WriteLine($"Unknown command: {command}");
        PrintHelp();
        return 1;
}

static async Task<int> RunGenerate(string[] args, Dictionary<string, string> flags,
    string defaultYaml, string defaultFix, string defaultOut)
{
    if (args.Length < 2)
    {
        await Console.Error.WriteLineAsync("Usage: generate <language> [options]");
        return 1;
    }

    var language = args[1].ToLowerInvariant();
    var fixturesPath = flags.GetValueOrDefault("--fixtures", defaultFix);
    var outputDir = flags.GetValueOrDefault("--output", Path.Combine(defaultOut, language));
    var port = int.TryParse(flags.GetValueOrDefault("--port", "5080"), out var p) ? p : 5080;
    var yamlPath = flags.GetValueOrDefault("--yaml", defaultYaml);
    var endpoints = EndpointLoader.Load(yamlPath);

    if (flags.ContainsKey("--bake"))
    {
        var typeMap = EndpointTypeMap.Build(endpoints);
        Console.WriteLine("Baking fixtures...");
        FixtureBaker.Bake(fixturesPath, typeMap);
    }

    IServerGenerator? gen = language switch
    {
        "go" => new GoGenerator(),
        "python" => new PythonGenerator(),
        "js" or "javascript" => new JavaScriptGenerator(),
        "java" => new JavaGenerator(),
        "kotlin" => new KotlinGenerator(),
        "ruby" => new RubyGenerator(),
        "csharp" or "cs" or "c#" => new CSharpGenerator(),
        _ => null
    };

    if (gen is null)
    {
        await Console.Error.WriteLineAsync($"Unknown language: {args[1]}. Supported: go, python, javascript, java, kotlin, ruby, csharp");
        return 1;
    }

    Console.WriteLine($"Generating {gen.Language} server -> {outputDir}");
    Directory.CreateDirectory(outputDir);
    gen.Generate(endpoints, fixturesPath, outputDir, port);
    Console.WriteLine($"Done. See {outputDir}/README.md for run instructions.");
    return 0;
}

static void PrintHelp()
{
    Console.WriteLine("""
        EggIncognito.CodeGen

        Commands:
          bake [--fixtures <path>] [--yaml <path>]
              Convert Fixtures/*.json to *.binpb binary proto files.

          generate <language> [--output <dir>] [--fixtures <path>] [--port <n>] [--bake]
              Generate a complete mock server project.
              Languages: go, python, javascript, java, kotlin, ruby, csharp

        Examples:
          dotnet run --project EggIncognito.CodeGen -- bake
          dotnet run --project EggIncognito.CodeGen -- generate go
          dotnet run --project EggIncognito.CodeGen -- generate csharp
          dotnet run --project EggIncognito.CodeGen -- generate python --bake
        """);
}

static Dictionary<string, string> ParseFlags(string[] args)
{
    var flags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    int i = 0;
    while (i < args.Length)
    {
        if (args[i].StartsWith("--"))
        {
            if (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
            {
                flags[args[i]] = args[i + 1];
                i += 2;
            }
            else
            {
                flags[args[i]] = "true";
                i++;
            }
        }
        else
        {
            i++;
        }
    }
    return flags;
}
