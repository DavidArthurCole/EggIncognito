using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace EggIncognito.CodeGen;

internal static class TemplateLoader
{
    private static readonly Assembly _asm = typeof(TemplateLoader).Assembly;

    internal static string Load(string language, string filename, IReadOnlyDictionary<string, string>? subs = null)
    {
        var name = $"EggIncognito.CodeGen.Templates.{language}.{filename}";
        using var stream = _asm.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Template not found: {name}");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var content = reader.ReadToEnd();
        if (subs is null) return content;
        foreach (var (key, value) in subs)
            content = content.Replace($"{{{key}}}", value);
        return content;
    }
}
