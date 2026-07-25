using EggIncognito.Bot;

namespace EggIncognito.Tests;

public class ProtoTruncationTests {
    [Fact]
    public void Truncate_ShortText_Unchanged() => Assert.Equal("hello", ProtoQuery.Truncate("hello"));

    [Fact]
    public void Truncate_ExactlyMax_Unchanged() {
        string text = new('x', ProtoQuery.MaxDescription);
        Assert.Same(text, ProtoQuery.Truncate(text));
    }

    [Fact]
    public void Truncate_LongText_ClampedWithMarker() {
        string text = new('x', ProtoQuery.MaxDescription + 5000);
        string result = ProtoQuery.Truncate(text);
        Assert.Equal(ProtoQuery.MaxDescription, result.Length);
        Assert.EndsWith("(truncated)", result);
    }

    [Fact]
    public void Truncate_CustomBudget_Respected() {
        string result = ProtoQuery.Truncate(new string('x', 100), 50);
        Assert.Equal(50, result.Length);
        Assert.EndsWith("(truncated)", result);
    }

    [Fact]
    public void Truncate_DefaultBudget_FitsEmbedLimitWithCodeFence() {
        string fenced = "```\n" + ProtoQuery.Truncate(new string('x', 50_000)) + "\n```";
        Assert.True(fenced.Length <= 4096);
    }
}
