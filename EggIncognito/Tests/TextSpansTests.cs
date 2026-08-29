using EggIncognito.Core.Services.Syntax;

namespace EggIncognito.Tests;

public class TextSpansTests {
    private static string Concat(string text, IReadOnlyList<Segment> segments) =>
        string.Concat(segments.Select(s => text.Substring(s.Start, s.Length)));

    [Fact]
    public void Slice_EmptySpanList_YieldsOneUnclassedSegment() {
        var segments = TextSpans.Slice("hello", null);
        Assert.Single(segments);
        Assert.Null(segments[0].Class);
        Assert.Equal("hello", Concat("hello", segments));
    }

    [Fact]
    public void Slice_CoversTheWholeStringExactly() {
        const string text = "abcdefghij";
        var spans = new List<Span> { new(2, 3, "a"), new(7, 2, "b") };
        var segments = TextSpans.Slice(text, spans);
        Assert.Equal(text, Concat(text, segments));
        Assert.Equal(["a", "b"], segments.Where(s => s.Class is not null).Select(s => s.Class));
    }

    [Fact]
    public void Slice_AdjacentSpans_ProduceNoEmptyGap() {
        const string text = "abcdef";
        var spans = new List<Span> { new(0, 3, "a"), new(3, 3, "b") };
        var segments = TextSpans.Slice(text, spans);
        Assert.Equal(2, segments.Count);
        Assert.Equal(text, Concat(text, segments));
    }

    [Fact]
    public void Slice_SpanPastTheEnd_IsClamped() {
        const string text = "abc";
        var spans = new List<Span> { new(1, 99, "a"), new(50, 2, "b") };
        var segments = TextSpans.Slice(text, spans);
        Assert.Equal(text, Concat(text, segments));
        Assert.All(segments, s => Assert.True(s.Start + s.Length <= text.Length));
    }

    [Fact]
    public void Slice_NegativeStart_IsClamped() {
        const string text = "abc";
        var segments = TextSpans.Slice(text, [new Span(-5, 7, "a")]);
        Assert.Equal(text, Concat(text, segments));
        Assert.Equal(0, segments[0].Start);
    }

    [Fact]
    public void Slice_EmptyText_YieldsNothing() {
        Assert.Empty(TextSpans.Slice("", [new Span(0, 3, "a")]));
        Assert.Empty(TextSpans.Slice(null, null));
    }

    [Fact]
    public void Clean_OverlappingSpansInOneLayer_AreClippedNotDropped() {
        var cleaned = TextSpans.Clean([new Span(0, 5, "a"), new Span(3, 5, "b")], 10);
        Assert.Equal(2, cleaned.Count);
        Assert.Equal(new Span(0, 5, "a"), cleaned[0]);
        Assert.Equal(new Span(5, 3, "b"), cleaned[1]);
    }

    [Fact]
    public void Layer_OverWinsAndUnderIsSplitAroundIt() {
        var layered = TextSpans.Layer([new Span(0, 10, "under")], [new Span(3, 4, "over")], 10);
        Assert.Equal(3, layered.Count);
        Assert.Equal(new Span(0, 3, "under"), layered[0]);
        Assert.Equal(new Span(3, 4, "over"), layered[1]);
        Assert.Equal(new Span(7, 3, "under"), layered[2]);
    }

    [Fact]
    public void Layer_OverCoveringUnderEntirely_RemovesUnder() {
        var layered = TextSpans.Layer([new Span(2, 3, "under")], [new Span(0, 10, "over")], 10);
        Assert.Single(layered);
        Assert.Equal("over", layered[0].Class);
    }

    [Fact]
    public void Layer_EmptyLayers_ReturnTheOther() {
        Assert.Single(TextSpans.Layer([new Span(0, 2, "u")], null, 10));
        Assert.Single(TextSpans.Layer(null, [new Span(0, 2, "o")], 10));
        Assert.Empty(TextSpans.Layer(null, null, 10));
    }

    [Fact]
    public void Layer_ResultIsOrderedAndNonOverlapping() {
        var under = new List<Span> { new(0, 4, "u1"), new(4, 4, "u2"), new(8, 4, "u3") };
        var over = new List<Span> { new(2, 3, "o1"), new(9, 2, "o2") };
        var layered = TextSpans.Layer(under, over, 12);
        int pos = 0;
        foreach (var s in layered) {
            Assert.True(s.Start >= pos);
            pos = s.Start + s.Length;
        }
    }

    [Fact]
    public void Find_ReturnsEveryOccurrence() {
        var found = TextSpans.Find("abcabcabc", ["abc"], "code-mark");
        Assert.Equal(3, found.Count);
        Assert.All(found, s => Assert.Equal("code-mark", s.Class));
    }

    [Fact]
    public void Find_IsCaseInsensitiveByDefault() {
        Assert.Single(TextSpans.Find("Hello", ["hello"], "code-mark"));
        Assert.Empty(TextSpans.Find("Hello", ["hello"], "code-mark", StringComparison.Ordinal));
    }

    [Fact]
    public void Find_EmptyInputs_ReturnNothing() {
        Assert.Empty(TextSpans.Find("", ["a"], "m"));
        Assert.Empty(TextSpans.Find("abc", [], "m"));
        Assert.Empty(TextSpans.Find("abc", [""], "m"));
    }
}
