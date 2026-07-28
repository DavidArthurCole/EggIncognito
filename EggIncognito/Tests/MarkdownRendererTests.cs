using EggIncognito.Services;

namespace EggIncognito.Tests;

public class MarkdownRendererTests {
    [Fact]
    public void Script_IsEscaped_NeverRealTag() {
        string html = MarkdownRenderer.Render("<script>alert(1)</script>");
        Assert.Contains("&lt;script&gt;", html);
        Assert.DoesNotContain("<script>", html);
    }

    [Fact]
    public void Link_JavascriptScheme_BecomesHash() {
        string html = MarkdownRenderer.Render("[x](javascript:alert(1))");
        Assert.Contains("href=\"#\"", html);
        Assert.DoesNotContain("javascript:", html);
    }

    [Fact]
    public void Link_DataScheme_BecomesHash() {
        string html = MarkdownRenderer.Render("[x](data:text/html,evil)");
        Assert.Contains("href=\"#\"", html);
        Assert.DoesNotContain("data:", html);
    }

    [Fact]
    public void Link_Https_Kept_WithSafeRel() {
        string html = MarkdownRenderer.Render("[x](https://example.com)");
        Assert.Contains("href=\"https://example.com\"", html);
        Assert.Contains("target=\"_blank\" rel=\"noopener noreferrer\"", html);
        Assert.Contains(">x</a>", html);
    }

    [Theory]
    [InlineData("/api/docs/image/1")]
    [InlineData("./foo")]
    [InlineData("#anchor")]
    public void Link_RelativeAndAnchor_Allowed(string url) {
        string html = MarkdownRenderer.Render($"[x]({url})");
        Assert.Contains($"href=\"{url}\"", html);
    }

    [Fact]
    public void Image_SafeUrl_AndAlt() {
        string html = MarkdownRenderer.Render("![cat](https://example.com/c.png)");
        Assert.Contains("<img src=\"https://example.com/c.png\" alt=\"cat\" />", html);
    }

    [Fact]
    public void Image_UnsafeUrl_BecomesHash() {
        string html = MarkdownRenderer.Render("![x](javascript:alert(1))");
        Assert.Contains("src=\"#\"", html);
        Assert.DoesNotContain("javascript:", html);
    }

    [Fact]
    public void Bold_Italic_Code() {
        Assert.Contains("<strong>bold</strong>", MarkdownRenderer.Render("**bold**"));
        Assert.Contains("<em>italic</em>", MarkdownRenderer.Render("*italic*"));
        Assert.Contains("<code>code</code>", MarkdownRenderer.Render("`code`"));
    }

    [Fact]
    public void Headings_H1ToH3() {
        Assert.Contains("<h1 class=\"md-h1\">", MarkdownRenderer.Render("# H1"));
        Assert.Contains("<h2 class=\"md-h2\">", MarkdownRenderer.Render("## H2"));
        Assert.Contains("<h3 class=\"md-h3\">", MarkdownRenderer.Render("### H3"));
    }

    [Fact]
    public void Heading_InlineFormattingApplied() {
        string html = MarkdownRenderer.Render("# Hello **world**");
        Assert.Contains("<h1 class=\"md-h1\">Hello <strong>world</strong></h1>", html);
    }

    [Fact]
    public void FencedCode_Block() {
        string html = MarkdownRenderer.Render("```\nline1\nline2\n```");
        Assert.Contains("<pre class=\"md-code\"><code>", html);
        Assert.Contains("line1\nline2", html);
        Assert.Contains("</code></pre>", html);
    }

    [Fact]
    public void UnorderedList_TwoItems() {
        string html = MarkdownRenderer.Render("- a\n- b");
        Assert.Contains("<ul class=\"md-list\">", html);
        Assert.Contains("<li>a</li>", html);
        Assert.Contains("<li>b</li>", html);
        Assert.Contains("</ul>", html);
    }

    [Fact]
    public void OrderedList() {
        string html = MarkdownRenderer.Render("1. a\n2. b");
        Assert.Contains("<ol class=\"md-list\">", html);
        Assert.Contains("<li>a</li>", html);
        Assert.Contains("</ol>", html);
    }

    [Fact]
    public void Blockquote() {
        string html = MarkdownRenderer.Render("> quote");
        Assert.Contains("<blockquote class=\"md-quote\">quote</blockquote>", html);
    }

    [Fact]
    public void HorizontalRule() {
        string html = MarkdownRenderer.Render("---");
        Assert.Contains("<hr class=\"md-rule\" />", html);
    }

    [Fact]
    public void Code_BodyWithAngleBracket_IsEscaped() {
        string html = MarkdownRenderer.Render("```\n<b>x</b>\n```");
        Assert.Contains("&lt;b&gt;", html);
        Assert.DoesNotContain("<b>", html);
    }

    [Fact]
    public void Quote_BodyWithAngleBracket_IsEscaped() {
        string html = MarkdownRenderer.Render("> <b>x</b>");
        Assert.Contains("&lt;b&gt;", html);
        Assert.DoesNotContain("<b>", html);
    }

    [Fact]
    public void Paragraph_MergesLinesWithBreak() {
        string html = MarkdownRenderer.Render("one\ntwo");
        Assert.Contains("<p>one<br/>two</p>", html);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NullOrEmpty_ReturnsEmpty_NoThrow(string? src) => Assert.Equal("", MarkdownRenderer.Render(src));

    [Fact]
    public void Link_QuoteInUrl_CannotBreakOutOfHrefAttribute() {
        string html = MarkdownRenderer.Render("[x](https://example.com/\" onmouseover=\"alert(1))");
        Assert.DoesNotContain("onmouseover=\"", html);
        Assert.Contains("&quot;", html);
    }

    [Fact]
    public void Image_QuoteInUrl_CannotBreakOutOfSrcAttribute() {
        string html = MarkdownRenderer.Render("![x](https://example.com/\" onerror=\"alert(1))");
        Assert.DoesNotContain("onerror=\"", html);
        Assert.Contains("&quot;", html);
    }

    [Fact]
    public void Image_QuoteInAltText_CannotBreakOutOfAltAttribute() {
        string html = MarkdownRenderer.Render("![a\"b](https://example.com/c.png)");
        Assert.Contains("alt=\"a&quot;b\"", html);
    }
}
