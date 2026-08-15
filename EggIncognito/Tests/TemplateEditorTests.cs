using EggIncognito.Services.Feed;

namespace EggIncognito.Tests;

public class TemplateEditorTests {
    [Fact]
    public void Tokens_AreEmptyForNullOrBlankOrPlainText() {
        Assert.Empty(FeedTemplate.Tokens(null));
        Assert.Empty(FeedTemplate.Tokens(""));
        Assert.Empty(FeedTemplate.Tokens("no variables at all"));
    }

    [Fact]
    public void Tokens_FindOne() {
        Assert.Equal(new[] { "appVersion" }, FeedTemplate.Tokens("New build {{appVersion}} is up!"));
    }

    [Fact]
    public void Tokens_KeepFirstAppearanceOrder() {
        Assert.Equal(
            new[] { "appVersion", "build", "platform" },
            FeedTemplate.Tokens("{{appVersion}} ({{build}}) on {{platform}}"));
    }

    [Fact]
    public void Tokens_CollapseRepeats() {
        Assert.Equal(new[] { "build", "platform" }, FeedTemplate.Tokens("{{build}} {{platform}} {{build}}"));
    }

    [Fact]
    public void Tokens_IgnoreMalformedBraces() {
        Assert.Empty(FeedTemplate.Tokens("{appVersion} {{ build }} {{}}"));
    }

    [Fact]
    public void Tokens_AreCaseSensitiveSoUnknownStaysUnknown() {
        Assert.Equal(new[] { "AppVersion" }, FeedTemplate.Tokens("{{AppVersion}}"));
    }

    [Fact]
    public void UnknownTokens_AreTheSetDifferenceAgainstTheKindsVars() {
        string[] known = ["platform", "appVersion", "build"];
        var used = FeedTemplate.Tokens("{{appVersion}} on {{platform}} at {{eta}} for {{whoever}}");

        string[] unknown = [.. used.Where(t => !known.Contains(t, StringComparer.Ordinal))];

        Assert.Equal(new[] { "eta", "whoever" }, unknown);
    }

    [Fact]
    public void UnknownTokens_AreEmptyWhenEveryVarIsKnown() {
        string[] known = ["feed", "sha", "pageUrl"];
        var used = FeedTemplate.Tokens("{{feed}} changed (hash {{sha}}) - see {{pageUrl}}");

        Assert.DoesNotContain(used, t => !known.Contains(t, StringComparer.Ordinal));
    }

    [Fact]
    public void Tokens_AgreeWithRender() {
        const string template = "{{appVersion}} {{build}} {{appVersion}}";
        var vars = FeedTemplate.Tokens(template).ToDictionary(t => t, _ => "x", StringComparer.Ordinal);

        Assert.Equal("x x x", FeedTemplate.Render(template, vars));
    }
}
