using System.Text.Json;
using EggIncognito.Models.Contracts;

namespace EggIncognito.Tests.Contracts;

public class ContractBackfillTests {
    private static readonly JsonSerializerOptions CarpetJson = new(JsonSerializerDefaults.Web);

    [Fact]
    public void CarpetJson_ParsesArrayIntoRows() {
        string json = """
        [
            { "id": "c1", "proto": "abc123==" },
            { "id": "c2", "proto": "def456==" }
        ]
        """;
        var rows = JsonSerializer.Deserialize<List<CarpetContract>>(json, CarpetJson);
        Assert.NotNull(rows);
        Assert.Equal(2, rows!.Count);
        Assert.Equal("c1", rows[0].Id);
        Assert.Equal("abc123==", rows[0].Proto);
        Assert.Equal("c2", rows[1].Id);
        Assert.Equal("def456==", rows[1].Proto);
    }
}
