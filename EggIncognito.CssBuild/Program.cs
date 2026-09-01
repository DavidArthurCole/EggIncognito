using EggIdentity.Styles;
using EggIncognito.CssBuild;
using MonorailCss;
using MonorailCss.Parser.SourceCss;

if (args.Length < 1) {
    Console.Error.WriteLine("Usage: EggIncognito.CssBuild <EggIncognito app project directory>");
    return 1;
}

var appProjectDir = Path.GetFullPath(args[0]);
if (!Directory.Exists(appProjectDir)) {
    Console.Error.WriteLine($"App project directory not found: {appProjectDir}");
    return 1;
}

var cssSourcePath = Path.Combine(appProjectDir, "Styles", "app.v4.css");
if (!File.Exists(cssSourcePath)) {
    Console.Error.WriteLine($"CSS source file not found: {cssSourcePath}");
    return 1;
}

var outputPath = Path.Combine(appProjectDir, "wwwroot", "styles.css");
var componentsDir = Path.Combine(appProjectDir, "Components");
var wwwrootDir = Path.Combine(appProjectDir, "wwwroot");
var markdownRendererPath = Path.GetFullPath(Path.Combine(appProjectDir, "..", "EggIncognito.Core", "Services", "MarkdownRenderer.cs"));

var contentFiles = new List<string>();
if (Directory.Exists(componentsDir)) contentFiles.AddRange(Directory.EnumerateFiles(componentsDir, "*.razor", SearchOption.AllDirectories));
if (Directory.Exists(wwwrootDir)) {
    contentFiles.AddRange(Directory.EnumerateFiles(wwwrootDir, "*.html", SearchOption.AllDirectories));
    contentFiles.AddRange(Directory.EnumerateFiles(wwwrootDir, "*.js", SearchOption.AllDirectories));
}
contentFiles.AddRange(Directory.EnumerateFiles(appProjectDir, "*.cs", SearchOption.AllDirectories).Where(path => !IsExcludedCsPath(appProjectDir, path)));
if (File.Exists(markdownRendererPath)) contentFiles.Add(markdownRendererPath);

var rawSourceText = File.ReadAllText(cssSourcePath);
var applyGuardViolation = CssBuildText.FindSemicolonInsideApplyBracket(rawSourceText);
if (applyGuardViolation is { } violation) {
    Console.Error.WriteLine($"CSS build guard failed: {cssSourcePath}:{violation.Line} has a ';' inside a bracket value within an @apply body, near: {violation.Snippet}");
    Console.Error.WriteLine("Move that ';' outside the bracket value and rebuild.");
    Console.Error.WriteLine("Static text guard only; cannot detect rules already mangled upstream by the parser.");
    return 1;
}

Console.WriteLine($"Scanning {contentFiles.Count} content files for utility/component class tokens...");
var candidates = CssBuildText.Scan(contentFiles);
candidates.UnionWith(ContentSafelist.Tokens);
Console.WriteLine($"Found {candidates.Count} distinct candidate tokens.");

var processor = new CssSourceProcessor(message => Console.WriteLine($"[monorail] {message}"));
var sourceResult = processor.ProcessFile(cssSourcePath, null);

var mergedApplies = ComponentClasses.All.SetItems(sourceResult.Settings.Applies);
var settings = sourceResult.Settings with { Applies = mergedApplies, IncludePreflight = false };

var framework = new CssFramework(settings);
var compiledCss = framework.Process(candidates);

var strippedRawCss = CssBuildText.UnwrapLayersAndSpliceRaw(CssBuildText.StripApplyDirectives(sourceResult.RawCss), "");

var finalCss = CssBuildText.UnwrapLayersAndSpliceRaw(compiledCss, strippedRawCss);

Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
File.WriteAllText(outputPath, finalCss);

Console.WriteLine($"Wrote {finalCss.Length} chars to {outputPath}");
return 0;

static bool IsExcludedCsPath(string rootDir, string path) {
    var relativePath = Path.GetRelativePath(rootDir, path);
    var segments = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    return segments.Any(segment => segment.Equals("bin", StringComparison.OrdinalIgnoreCase) || segment.Equals("obj", StringComparison.OrdinalIgnoreCase) || segment.Equals("Tests", StringComparison.OrdinalIgnoreCase));
}
