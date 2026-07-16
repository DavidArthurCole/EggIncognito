using System.Text.RegularExpressions;

namespace EggIncognito.Services;
//


public static class MarkdownRenderer
{
    static readonly Regex EscapeChars = new("[&<>\"']", RegexOptions.Compiled);
    static readonly Regex SafeScheme = new(@"^(https?://|/|\./|#)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex SplitLines = new(@"\r?\n", RegexOptions.Compiled);

    static readonly Regex InlineCode = new(@"`([^`]+)`", RegexOptions.Compiled);
    static readonly Regex InlineImg = new(@"!\[([^\]]*)\]\(([^)]+)\)", RegexOptions.Compiled);
    static readonly Regex InlineLink = new(@"\[([^\]]+)\]\(([^)]+)\)", RegexOptions.Compiled);
    static readonly Regex InlineBold = new(@"\*\*([^*]+)\*\*", RegexOptions.Compiled);
    static readonly Regex InlineItalic = new(@"(^|[^*])\*([^*]+)\*", RegexOptions.Compiled);

    static readonly Regex Fence = new(@"^```", RegexOptions.Compiled);
    static readonly Regex Rule = new(@"^\s*---+\s*$", RegexOptions.Compiled);
    static readonly Regex Heading = new(@"^(#{1,3})\s+(.*)$", RegexOptions.Compiled);
   
   
    static readonly Regex Quote = new(@"^\s*&gt;\s?", RegexOptions.Compiled);
    static readonly Regex Ul = new(@"^\s*[-*]\s+", RegexOptions.Compiled);
    static readonly Regex Ol = new(@"^\s*\d+\.\s+", RegexOptions.Compiled);
   
   
    static readonly Regex ParaStop = new(@"^(#{1,3}\s|\s*[-*]\s|\s*\d+\.\s|\s*&gt;|```|\s*---+\s*$)", RegexOptions.Compiled);

    static string EscapeHtml(string s) => EscapeChars.Replace(s, m => m.Value switch
    {
        "&" => "&amp;",
        "<" => "&lt;",
        ">" => "&gt;",
        "\"" => "&quot;",
        "'" => "&#39;",
        _ => m.Value
    });

   
    static string SafeUrl(string url)
    {
        var u = url.Trim();
        return SafeScheme.IsMatch(u) ? u : "#";
    }

   
   
    static string Inline(string text)
    {
        text = InlineCode.Replace(text, m => $"<code>{m.Groups[1].Value}</code>");
        text = InlineImg.Replace(text, m => $"<img src=\"{SafeUrl(m.Groups[2].Value)}\" alt=\"{m.Groups[1].Value}\" />");
        text = InlineLink.Replace(text, m => $"<a href=\"{SafeUrl(m.Groups[2].Value)}\" target=\"_blank\" rel=\"noopener noreferrer\">{m.Groups[1].Value}</a>");
        text = InlineBold.Replace(text, "<strong>$1</strong>");
        text = InlineItalic.Replace(text, "$1<em>$2</em>");
        return text;
    }

   
    public static string Render(string? src)
    {
        var lines = SplitLines.Split(EscapeHtml(src ?? ""));
        var outLines = new List<string>();
        var i = 0;
        string? listType = null;

        void CloseList()
        {
            if (listType != null) { outLines.Add($"</{listType}>"); listType = null; }
        }

        while (i < lines.Length)
        {
            var line = lines[i];

            if (Fence.IsMatch(line.Trim()))
            {
                CloseList();
                var body = new List<string>();
                i++;
                while (i < lines.Length && !Fence.IsMatch(lines[i].Trim())) { body.Add(lines[i]); i++; }
                i++;
                outLines.Add($"<pre class=\"md-code\"><code>{string.Join("\n", body)}</code></pre>");
                continue;
            }

            if (Rule.IsMatch(line)) { CloseList(); outLines.Add("<hr class=\"md-rule\" />"); i++; continue; }

            var h = Heading.Match(line);
            if (h.Success)
            {
                CloseList();
                var n = h.Groups[1].Value.Length;
                outLines.Add($"<h{n} class=\"md-h{n}\">{Inline(h.Groups[2].Value)}</h{n}>");
                i++;
                continue;
            }

           
            if (Quote.IsMatch(line))
            {
                CloseList();
                var body = new List<string>();
                while (i < lines.Length && Quote.IsMatch(lines[i])) { body.Add(Quote.Replace(lines[i], "", 1)); i++; }
                outLines.Add($"<blockquote class=\"md-quote\">{Inline(string.Join("<br/>", body))}</blockquote>");
                continue;
            }

            if (Ul.IsMatch(line))
            {
                if (listType != "ul") { CloseList(); outLines.Add("<ul class=\"md-list\">"); listType = "ul"; }
                outLines.Add($"<li>{Inline(Ul.Replace(line, "", 1))}</li>");
                i++;
                continue;
            }
            if (Ol.IsMatch(line))
            {
                if (listType != "ol") { CloseList(); outLines.Add("<ol class=\"md-list\">"); listType = "ol"; }
                outLines.Add($"<li>{Inline(Ol.Replace(line, "", 1))}</li>");
                i++;
                continue;
            }

            if (line.Trim() == "") { CloseList(); i++; continue; }

            {
                CloseList();
                var body = new List<string> { line };
                i++;
                while (i < lines.Length && lines[i].Trim() != "" && !ParaStop.IsMatch(lines[i]))
                {
                    body.Add(lines[i]);
                    i++;
                }
                outLines.Add($"<p>{Inline(string.Join("<br/>", body))}</p>");
            }
        }
        CloseList();
        return string.Join("\n", outLines);
    }
}
