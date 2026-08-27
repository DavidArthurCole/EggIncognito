using EggIncognito.Services.Workbench;

namespace EggIncognito.Tests;

public class WorkbenchStatusTests {
    [Theory]
    [InlineData(WorkbenchStatusKind.Queued, "wb-st-queued")]
    [InlineData(WorkbenchStatusKind.Running, "wb-st-run")]
    [InlineData(WorkbenchStatusKind.Done, "wb-st-done")]
    [InlineData(WorkbenchStatusKind.Error, "wb-st-err")]
    [InlineData(WorkbenchStatusKind.Info, "wb-st-offer")]
    [InlineData(WorkbenchStatusKind.Muted, "wb-st-muted")]
    public void Class_MapsEveryKind(WorkbenchStatusKind kind, string expected) => Assert.Equal(expected, WorkbenchStatus.Class(kind));

    [Fact]
    public void Class_CoversTheWholeEnum() {
        foreach (WorkbenchStatusKind kind in Enum.GetValues<WorkbenchStatusKind>()) {
            Assert.StartsWith("wb-st-", WorkbenchStatus.Class(kind), StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("queued", WorkbenchStatusKind.Queued)]
    [InlineData("pending", WorkbenchStatusKind.Queued)]
    [InlineData("running", WorkbenchStatusKind.Running)]
    [InlineData("run", WorkbenchStatusKind.Running)]
    [InlineData("succeeded", WorkbenchStatusKind.Done)]
    [InlineData("done", WorkbenchStatusKind.Done)]
    [InlineData("ok", WorkbenchStatusKind.Done)]
    [InlineData("failed", WorkbenchStatusKind.Error)]
    [InlineData("error", WorkbenchStatusKind.Error)]
    [InlineData("err", WorkbenchStatusKind.Error)]
    [InlineData("info", WorkbenchStatusKind.Info)]
    [InlineData("offer", WorkbenchStatusKind.Info)]
    [InlineData("offerable", WorkbenchStatusKind.Info)]
    public void Parse_MapsTheSharedVocabulary(string value, WorkbenchStatusKind expected) => Assert.Equal(expected, WorkbenchStatus.Parse(value));

    [Theory]
    [InlineData("RUNNING", WorkbenchStatusKind.Running)]
    [InlineData("Failed", WorkbenchStatusKind.Error)]
    public void Parse_IsCaseInsensitive(string value, WorkbenchStatusKind expected) => Assert.Equal(expected, WorkbenchStatus.Parse(value));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abandoned")]
    [InlineData("whatever")]
    public void Parse_FallsBackToMuted(string? value) => Assert.Equal(WorkbenchStatusKind.Muted, WorkbenchStatus.Parse(value));
}
