using System.Collections;
using System.Reflection;
using System.Text;
using EggIncognito.Capture;

namespace EggIncognito.Build;

// Emits wwwroot/capture/types.d.ts describing the JSON shapes the dashboard backend sends over SSE
// and the REST API. Generated from the actual C# records by reflection, so the JS side has one source
// of truth and the editor flags a field-name typo or a renamed property. Property names are
// lower-camelCased to match JsonSerializerDefaults.Web.
//
// Invoked at build time by the EmitDashboardTypes MSBuild target in EggIncognito.csproj, which runs
// `dotnet run -- __emit-types <outPath>`. JS modules reference these via JSDoc @typedef imports.
public static class TypeEmitter
{
    // The record types the dashboard consumes. Add new wire types here.
    private static readonly Type[] Roots =
    [
        typeof(DashboardFlow),
        typeof(DashboardHeader),
        typeof(CaptureStats),
    ];

    // outPath: the full path of the types.d.ts file to write (the MSBuild target supplies it).
    public static int Run(string outPath)
    {
        var emitted = new Dictionary<string, string>(StringComparer.Ordinal);
        var queue = new Queue<Type>(Roots);
        var seen = new HashSet<Type>();

        while (queue.Count > 0)
        {
            var t = queue.Dequeue();
            if (!seen.Add(t)) continue;
            emitted[t.Name] = EmitType(t, queue);
        }

        var sb = new StringBuilder();
        sb.AppendLine("// AUTO-GENERATED at build time from the C# records in EggIncognito.Capture.");
        sb.AppendLine("// Do NOT edit by hand; it is regenerated on every build. Field names are camelCased");
        sb.AppendLine("// to match the JsonSerializerDefaults.Web casing used on the wire.");
        sb.AppendLine();
        // Stable order: roots first in declared order, then the rest alphabetical.
        foreach (var name in Roots.Select(r => r.Name).Concat(emitted.Keys.Except(Roots.Select(r => r.Name)).OrderBy(x => x)))
            if (emitted.TryGetValue(name, out var body))
                sb.AppendLine(body);

        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        var text = sb.ToString();
        // Skip the write and its mtime bump when nothing changed, so the build target stays
        // incremental and does not re-trigger downstream work every build.
        if (File.Exists(outPath) && File.ReadAllText(outPath) == text) return 0;
        File.WriteAllText(outPath, text, new UTF8Encoding(false));
        Console.WriteLine($"Wrote {outPath} ({emitted.Count} types)");
        return 0;
    }

    private static string EmitType(Type t, Queue<Type> queue)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"export interface {t.Name} {{");
        // Records expose their data as public instance properties, one per positional/init member.
        foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (p.GetIndexParameters().Length > 0) continue; // skip indexers
            if (p.Name == "EqualityContract") continue;
            var (ts, optional) = TsType(p.PropertyType, queue);
            sb.AppendLine($"  {CamelCase(p.Name)}{(optional ? "?" : "")}: {ts};");
        }
        sb.AppendLine("}");
        return sb.ToString();
    }

    // Map a CLR type to a TS type. Enqueues nested record types for emission. Returns (tsType,
    // isOptional); reference/nullable types are optional since the JSON may omit or null them.
    private static (string ts, bool optional) TsType(Type t, Queue<Type> queue)
    {
        var underlying = Nullable.GetUnderlyingType(t);
        if (underlying is not null) return (TsType(underlying, queue).ts, true);

        if (t == typeof(string)) return ("string", true);
        if (t == typeof(bool)) return ("boolean", false);
        if (t == typeof(int) || t == typeof(long) || t == typeof(uint) || t == typeof(ulong)
            || t == typeof(double) || t == typeof(float) || t == typeof(decimal) || t == typeof(short))
            return ("number", false);

        // List<T> / IReadOnlyList<T> / arrays -> T[]
        if (t.IsArray)
        {
            var (el, _) = TsType(t.GetElementType()!, queue);
            return ($"{el}[]", true);
        }
        if (t.IsGenericType && typeof(IEnumerable).IsAssignableFrom(t))
        {
            var arg = t.GetGenericArguments()[0];
            var (el, _) = TsType(arg, queue);
            return ($"{el}[]", true);
        }

        // A nested record/class we also emit.
        if (t.Namespace?.StartsWith("EggIncognito") == true)
        {
            queue.Enqueue(t);
            return (t.Name, true);
        }

        return ("unknown", true);
    }

    private static string CamelCase(string s) =>
        string.IsNullOrEmpty(s) || char.IsLower(s[0]) ? s : char.ToLowerInvariant(s[0]) + s[1..];
}
