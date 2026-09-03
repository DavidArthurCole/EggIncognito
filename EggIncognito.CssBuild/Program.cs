using EggIdentity.Styles;
using MonorailCss;
using MonorailCss.Parser.SourceCss;

namespace EggIncognito.CssBuild;

public static class Program {
    private static readonly string[] ExcludedCsFolders = ["bin", "obj", "Tests"];

    public static int Main(string[] args) {
        if (args.Length < 1) {
            Console.Error.WriteLine("Usage: EggIncognito.CssBuild <EggIncognito app project directory>");
            return 1;
        }

        string appProjectDir = Path.GetFullPath(args[0]);
        if (!Directory.Exists(appProjectDir)) {
            Console.Error.WriteLine($"App project directory not found: {appProjectDir}");
            return 1;
        }

        string cssSourcePath = Path.Combine(appProjectDir, "Styles", "app.v4.css");
        if (!File.Exists(cssSourcePath)) {
            Console.Error.WriteLine($"CSS source file not found: {cssSourcePath}");
            return 1;
        }

        string rawSourceText = File.ReadAllText(cssSourcePath);
        if (CssBuildText.FindSemicolonInsideApplyBracket(rawSourceText) is { } violation) {
            Console.Error.WriteLine($"CSS build guard failed: {cssSourcePath}:{violation.Line} has a ';' inside a "
                                    + $"bracket value within an @apply body, near: {violation.Snippet}");
            Console.Error.WriteLine("Move that ';' outside the bracket value and rebuild.");
            Console.Error.WriteLine(
                "Static text guard only; cannot detect rules already mangled upstream by the parser.");
            return 1;
        }

        var contentFiles = ContentFiles(appProjectDir);
        Console.WriteLine($"Scanning {contentFiles.Count} content files for utility/component class tokens...");
        var candidates = CssBuildText.Scan(contentFiles);
        candidates.UnionWith(ContentSafelist.Tokens);
        Console.WriteLine($"Found {candidates.Count} distinct candidate tokens.");

        string outputPath = Path.Combine(appProjectDir, "wwwroot", "styles.css");
        string finalCss = Compile(cssSourcePath, candidates);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.WriteAllText(outputPath, finalCss);
        Console.WriteLine($"Wrote {finalCss.Length} chars to {outputPath}");
        return 0;
    }

    private static List<string> ContentFiles(string appProjectDir) {
        string componentsDir = Path.Combine(appProjectDir, "Components");
        string wwwrootDir = Path.Combine(appProjectDir, "wwwroot");
        string markdownRendererPath = Path.GetFullPath(
            Path.Combine(appProjectDir, "..", "EggIncognito.Core", "Services", "MarkdownRenderer.cs"));

        var contentFiles = new List<string>();
        if (Directory.Exists(componentsDir))
            contentFiles.AddRange(Directory.EnumerateFiles(componentsDir, "*.razor", SearchOption.AllDirectories));
        if (Directory.Exists(wwwrootDir)) {
            contentFiles.AddRange(Directory.EnumerateFiles(wwwrootDir, "*.html", SearchOption.AllDirectories));
            contentFiles.AddRange(Directory.EnumerateFiles(wwwrootDir, "*.js", SearchOption.AllDirectories));
        }

        contentFiles.AddRange(Directory.EnumerateFiles(appProjectDir, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsExcludedCsPath(appProjectDir, path)));
        if (File.Exists(markdownRendererPath)) contentFiles.Add(markdownRendererPath);
        return contentFiles;
    }

    private static string Compile(string cssSourcePath, IEnumerable<string> candidates) {
        var processor = new CssSourceProcessor(message => Console.WriteLine($"[monorail] {message}"));
        var sourceResult = processor.ProcessFile(cssSourcePath, null);
        var mergedApplies = ComponentClasses.All.SetItems(sourceResult.Settings.Applies);
        var settings = sourceResult.Settings with { Applies = mergedApplies, IncludePreflight = false };

        string compiledCss = new CssFramework(settings).Process(candidates);
        string strippedRawCss = CssBuildText.UnwrapLayersAndSpliceRaw(
            CssBuildText.StripApplyDirectives(sourceResult.RawCss), "");
        return CssBuildText.UnwrapLayersAndSpliceRaw(compiledCss, strippedRawCss);
    }

    private static bool IsExcludedCsPath(string rootDir, string path) =>
        Path.GetRelativePath(rootDir, path)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment => ExcludedCsFolders.Contains(segment, StringComparer.OrdinalIgnoreCase));
}
