using EggIncognito.Core.Services.ProtoExtract;

namespace EggIncognito.Tests;

public class ProtoDiffSummaryTests {
    private const string Old = """
        message Foo {
            optional int32 id = 1;
        }
        """;

    private const string New = """
        message Foo {
            optional int64 id = 1;
            optional string name = 2;
        }
        message Bar {
            optional int32 x = 1;
        }
        """;

    [Fact]
    public void SummaryCountsMessagesFieldsAndLines() {
        var structural = ProtoDiff.Compute(Old, New);
        var ops = MyersDiff.Compute(UnifiedDiffWriter.SplitLines(Old), UnifiedDiffWriter.SplitLines(New));
        var summary = ProtoDiffSummary.From(structural, ops);
        Assert.Equal(1, summary.MessagesAdded);
        Assert.Equal(1, summary.MessagesModified);
        Assert.Equal(1, summary.FieldsAdded);
        Assert.Equal(1, summary.FieldsChanged);
        Assert.True(summary.LinesAdded > 0);
        Assert.False(summary.IsEmpty);
    }

    [Fact]
    public void IdenticalInputGivesEmptySummary() {
        var structural = ProtoDiff.Compute(Old, Old);
        var ops = MyersDiff.Compute(UnifiedDiffWriter.SplitLines(Old), UnifiedDiffWriter.SplitLines(Old));
        Assert.True(ProtoDiffSummary.From(structural, ops).IsEmpty);
    }
}
