using EggIncognito.Services.Syntax;

namespace EggIncognito.Tests;

public class SyntaxCacheTests {
    [Fact]
    public void SameTextAndLanguage_ReturnsTheSameInstance() {
        var cache = new SyntaxCache();
        var tokenizer = SyntaxHighlighter.Tokenizer("json");
        var first = cache.Get("{\"a\":1}", tokenizer);
        var second = cache.Get("{\"a\":1}", tokenizer);
        Assert.Same(first, second);
    }

    [Fact]
    public void DifferentText_ReturnsADifferentInstance() {
        var cache = new SyntaxCache();
        var tokenizer = SyntaxHighlighter.Tokenizer("json");
        Assert.NotSame(cache.Get("{\"a\":1}", tokenizer), cache.Get("{\"a\":2}", tokenizer));
    }

    [Fact]
    public void SameTextDifferentLanguage_ReturnsADifferentInstance() {
        var cache = new SyntaxCache();
        var json = cache.Get("a: 1", SyntaxHighlighter.Tokenizer("json"));
        var yaml = cache.Get("a: 1", SyntaxHighlighter.Tokenizer("yaml"));
        Assert.NotSame(json, yaml);
        Assert.Equal("json", json.Language);
        Assert.Equal("yaml", yaml.Language);
    }

    [Fact]
    public void EvictsByEntryCount() {
        var cache = new SyntaxCache(maxEntries: 2, maxChars: long.MaxValue);
        var tokenizer = SyntaxHighlighter.Tokenizer("text");
        var first = cache.Get("one", tokenizer);
        cache.Get("two", tokenizer);
        cache.Get("three", tokenizer);
        Assert.Equal(2, cache.Count);
        Assert.NotSame(first, cache.Get("one", tokenizer));
    }

    [Fact]
    public void EvictsByTotalCharacters() {
        var cache = new SyntaxCache(maxEntries: int.MaxValue, maxChars: 12);
        var tokenizer = SyntaxHighlighter.Tokenizer("text");
        cache.Get(new string('a', 10), tokenizer);
        cache.Get(new string('b', 10), tokenizer);
        Assert.True(cache.Chars <= 12 || cache.Count == 1);
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void RecentlyUsedEntrySurvivesEviction() {
        var cache = new SyntaxCache(maxEntries: 2, maxChars: long.MaxValue);
        var tokenizer = SyntaxHighlighter.Tokenizer("text");
        var first = cache.Get("one", tokenizer);
        cache.Get("two", tokenizer);
        cache.Get("one", tokenizer);
        cache.Get("three", tokenizer);
        Assert.Same(first, cache.Get("one", tokenizer));
    }

    [Fact]
    public void SharedCache_IsReusedByTheHighlighter() {
        Assert.Same(SyntaxHighlighter.Highlight("{\"x\":1}", "json"),
            SyntaxHighlighter.Highlight("{\"x\":1}", "json"));
    }
}
