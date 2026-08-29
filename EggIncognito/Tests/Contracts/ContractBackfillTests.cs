using System.Text.Json;
using EggIncognito.Models.Contracts;
using EggIncognito.Services.Contracts;
using Ei;
using Google.Protobuf;

namespace EggIncognito.Tests.Contracts;

public class ContractBackfillTests {
    private static readonly JsonSerializerOptions CarpetJson = new(JsonSerializerDefaults.Web);

    private static Contract BasicContract(string id = "c1") => new() {
        Identifier = id,
        Name = "Test Contract",
        StartTime = 1700000000,
        ExpirationTime = 1700100000,
        LengthSeconds = 100000
    };

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

    [Fact]
    public void FromCarpet_SkippedCount_MatchesMalformedRowCount() {
        var valid = BasicContract("valid-1");
        var rows = new List<CarpetContract> {
            new("valid-1", Convert.ToBase64String(valid.ToByteArray())),
            new("bad-1", "not-valid-base64!!!")
        };
        var observations = ContractMapper.FromCarpet(rows);
        int skipped = rows.Count - observations.Count;
        Assert.Single(observations);
        Assert.Equal(1, skipped);
    }
}
