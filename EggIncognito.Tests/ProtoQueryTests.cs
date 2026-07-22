using EggIncognito.Bot;
using EggIncognito.Services;

namespace EggIncognito.Tests;

public class ProtoQueryTests {
    private static readonly IReadOnlyList<string> Names =
        new List<string> { "Alpha", "Beta", "Gamma", "Delta", "Epsilon" };

    [Fact]
    public void Page_SlicesAndReportsTotal() {
        var (slice, page, pages) = ProtoQuery.Page(Names, requestedPage: 1, perPage: 2);
        Assert.Equal(new[] { "Alpha", "Beta" }, slice);
        Assert.Equal(1, page);
        Assert.Equal(3, pages);
    }

    [Fact]
    public void Page_ClampsOutOfRange() {
        var (slice, page, _) = ProtoQuery.Page(Names, requestedPage: 99, perPage: 2);
        Assert.Equal(3, page);
        Assert.Equal(new[] { "Epsilon" }, slice);
    }

    [Fact]
    public void Autocomplete_FiltersCaseInsensitiveContains_Max25() {
        var hits = ProtoQuery.Autocomplete(Names, "a");
        Assert.Contains("Alpha", hits);
        Assert.Contains("Gamma", hits);
        Assert.DoesNotContain("Epsilon", hits);
        Assert.True(hits.Count <= 25);
    }

    [Fact]
    public void TypeLines_FormatsFields() {
        var msg = new SchemaMessage("Foo", new List<SchemaField>
        {
            new("bar", "bar", 1, "string", false, false, null, null),
            new("kind", "kind", 2, "enum", false, false, null,
                new List<SchemaEnumValue> { new("A", 0), new("B", 1) }),
        });
        var text = ProtoQuery.TypeLines(msg);
        Assert.Contains("1", text);
        Assert.Contains("bar", text);
        Assert.Contains("string", text);
        Assert.Contains("kind", text);
        Assert.Contains("A", text);
    }
}
