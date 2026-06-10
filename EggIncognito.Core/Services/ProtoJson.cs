// Shared JSON formatting helpers for the proto-JSON the app reads + writes: a stable pretty-printer
// (deterministic 2-space layout, one object per line) and the float normalizer that makes "X.0" and
// "X" compare equal. Used by the extraction pipeline (endpoint file writes + same/diff compare) and
// the capture dashboard decoder, so both render proto JSON identically.

using System.Text;
using System.Text.RegularExpressions;

namespace EggIncognito.Services;

public static class ProtoJson
{
    // Collapse a "<int>.0" produced by the proto JSON formatter to "<int>" so an endpoint that only
    // differs by float formatting compares as the same content.
    public static string NormalizeFloats(string json) =>
        Regex.Replace(json, @"(?<=[:\[,\s])(-?\d+)\.0(?=[,\}\]\s\r\n])", "$1");

    // Deterministic pretty-printer. Hand-rolled (not System.Text.Json) so the layout is stable and
    // string contents are preserved verbatim.
    public static string PrettyPrint(string json)
    {
        var sb = new StringBuilder(json.Length * 2);
        int depth = 0, i = 0;
        bool inString = false, escape = false;
        while (i < json.Length)
        {
            char c = json[i++];
            if (escape) { sb.Append(c); escape = false; continue; }
            if (inString) { AppendInString(sb, c, ref inString, ref escape); continue; }
            AppendStructural(sb, json, c, ref i, ref depth, ref inString);
        }
        return sb.ToString();
    }

    private static void AppendInString(StringBuilder sb, char c, ref bool inString, ref bool escape)
    {
        sb.Append(c);
        if (c == '\\') escape = true;
        else if (c == '"') inString = false;
    }

    private static void AppendStructural(StringBuilder sb, string json, char c, ref int i, ref int depth, ref bool inString)
    {
        switch (c)
        {
            case ' ': case '\t': case '\r': case '\n': break;
            case '"': inString = true; sb.Append(c); break;
            case '{': case '[': AppendOpen(sb, json, c, ref i, ref depth); break;
            case '}': case ']': sb.AppendLine(); sb.Append(' ', --depth * 2); sb.Append(c); break;
            case ',': sb.Append(c); sb.AppendLine(); sb.Append(' ', depth * 2); break;
            case ':': sb.Append(": "); break;
            default: sb.Append(c); break;
        }
    }

    private static void AppendOpen(StringBuilder sb, string json, char open, ref int i, ref int depth)
    {
        sb.Append(open);
        int j = i;
        while (j < json.Length && char.IsWhiteSpace(json[j])) j++;
        if (j < json.Length && (json[j] == '}' || json[j] == ']'))
        {
            sb.Append(json[j]);
            i = j + 1;
        }
        else
        {
            sb.AppendLine();
            sb.Append(' ', ++depth * 2);
        }
    }
}
