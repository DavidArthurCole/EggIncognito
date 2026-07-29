using System.Text;
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
if (Directory.Exists(componentsDir)) {
    contentFiles.AddRange(Directory.EnumerateFiles(componentsDir, "*.razor", SearchOption.AllDirectories));
}
if (Directory.Exists(wwwrootDir)) {
    contentFiles.AddRange(Directory.EnumerateFiles(wwwrootDir, "*.html", SearchOption.AllDirectories));
    contentFiles.AddRange(Directory.EnumerateFiles(wwwrootDir, "*.js", SearchOption.AllDirectories));
}
contentFiles.AddRange(Directory.EnumerateFiles(appProjectDir, "*.cs", SearchOption.AllDirectories).Where(path => !IsExcludedCsPath(appProjectDir, path)));
if (File.Exists(markdownRendererPath)) {
    contentFiles.Add(markdownRendererPath);
}

var rawSourceText = File.ReadAllText(cssSourcePath);
var applyGuardViolation = FindSemicolonInsideApplyBracket(rawSourceText);
if (applyGuardViolation is { } violation) {
    Console.Error.WriteLine($"CSS build guard failed: {cssSourcePath}:{violation.Line} has a ';' inside a bracket value within an @apply body, near: {violation.Snippet}");
    Console.Error.WriteLine("Move that ';' outside the bracket value and rebuild.");
    Console.Error.WriteLine("Static text guard only; cannot detect rules already mangled upstream by the parser.");
    return 1;
}

Console.WriteLine($"Scanning {contentFiles.Count} content files for utility/component class tokens...");
var candidates = ContentScanner.Scan(contentFiles);
candidates.UnionWith(ContentSafelist.Tokens);
Console.WriteLine($"Found {candidates.Count} distinct candidate tokens.");

var processor = new CssSourceProcessor(message => Console.WriteLine($"[monorail] {message}"));
var sourceResult = processor.ProcessFile(cssSourcePath, null);

var mergedApplies = ComponentClasses.All.SetItems(sourceResult.Settings.Applies);
var settings = sourceResult.Settings with { Applies = mergedApplies, IncludePreflight = false };

var framework = new CssFramework(settings);
var compiledCss = framework.Process(candidates);

var strippedRawCss = UnwrapLayersAndSpliceRaw(StripApplyDirectives(sourceResult.RawCss), "");

var finalCss = UnwrapLayersAndSpliceRaw(compiledCss, strippedRawCss);

Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
File.WriteAllText(outputPath, finalCss);

Console.WriteLine($"Wrote {finalCss.Length} chars to {outputPath}");
return 0;

static bool IsExcludedCsPath(string rootDir, string path) {
    var relativePath = Path.GetRelativePath(rootDir, path);
    var segments = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    return segments.Any(segment => segment.Equals("bin", StringComparison.OrdinalIgnoreCase) || segment.Equals("obj", StringComparison.OrdinalIgnoreCase) || segment.Equals("Tests", StringComparison.OrdinalIgnoreCase));
}

static (int Line, string Snippet)? FindSemicolonInsideApplyBracket(string text) {
    var searchStart = 0;
    while (true) {
        var applyIndex = text.IndexOf("@apply", searchStart, StringComparison.Ordinal);
        if (applyIndex < 0) {
            return null;
        }
        var depth = 0;
        var pos = applyIndex + "@apply".Length;
        while (pos < text.Length) {
            var c = text[pos];
            if (c == '[') {
                depth++;
            } else if (c == ']') {
                depth = Math.Max(0, depth - 1);
            } else if (c == ';') {
                if (depth > 0) {
                    var line = 1;
                    for (var j = 0; j < pos; j++) {
                        if (text[j] == '\n') {
                            line++;
                        }
                    }
                    var snippetStart = Math.Max(applyIndex, pos - 40);
                    var snippet = text.Substring(snippetStart, pos - snippetStart + 1);
                    return (line, snippet);
                }
                break;
            }
            pos++;
        }
        searchStart = applyIndex + "@apply".Length;
    }
}

static string UnwrapLayersAndSpliceRaw(string compiled, string raw) {
    var result = new StringBuilder(compiled.Length + raw.Length + 16);
    var rawSpliced = false;
    var i = 0;
    while (i < compiled.Length) {
        var layerIndex = compiled.IndexOf("@layer", i, StringComparison.Ordinal);
        if (layerIndex < 0) {
            result.Append(compiled, i, compiled.Length - i);
            break;
        }
        result.Append(compiled, i, layerIndex - i);
        var headEnd = layerIndex + "@layer".Length;
        while (headEnd < compiled.Length && compiled[headEnd] != '{' && compiled[headEnd] != ';') {
            headEnd++;
        }
        if (headEnd >= compiled.Length) {
            break;
        }
        var layerName = compiled.Substring(layerIndex + "@layer".Length, headEnd - layerIndex - "@layer".Length).Trim();
        if (compiled[headEnd] == ';') {
            i = headEnd + 1;
            continue;
        }
        var depth = 1;
        var bodyStart = headEnd + 1;
        var pos = bodyStart;
        while (pos < compiled.Length && depth > 0) {
            var c = compiled[pos];
            if (c == '{') {
                depth++;
            } else if (c == '}') {
                depth--;
            }
            pos++;
        }
        var bodyEnd = pos - 1;
        result.Append(compiled, bodyStart, bodyEnd - bodyStart);
        if (!rawSpliced && layerName == "components") {
            result.Append('\n');
            result.Append(raw);
            result.Append('\n');
            rawSpliced = true;
        }
        i = pos;
    }
    if (!rawSpliced) {
        result.Append('\n');
        result.Append(raw);
    }
    return result.ToString();
}

static string StripApplyDirectives(string css) {
    var result = new StringBuilder(css.Length);
    var i = 0;
    while (true) {
        var applyIndex = css.IndexOf("@apply", i, StringComparison.Ordinal);
        if (applyIndex < 0) {
            result.Append(css, i, css.Length - i);
            return result.ToString();
        }
        result.Append(css, i, applyIndex - i);
        var depth = 0;
        var pos = applyIndex + "@apply".Length;
        var terminatorFound = false;
        while (pos < css.Length) {
            var c = css[pos];
            if (c == '[') {
                depth++;
            } else if (c == ']') {
                depth = Math.Max(0, depth - 1);
            } else if (c == ';' && depth == 0) {
                pos++;
                terminatorFound = true;
                break;
            }
            pos++;
        }
        if (!terminatorFound) {
            result.Append(css, applyIndex, css.Length - applyIndex);
            return result.ToString();
        }
        i = pos;
    }
}
