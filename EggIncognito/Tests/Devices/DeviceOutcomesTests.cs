using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Tests.Devices;

public class DeviceOutcomesTests {
    [Theory]
    [InlineData(DeviceOutcome.Ok, DeviceOutcomes.Ok)]
    [InlineData(DeviceOutcome.Unsupported, DeviceOutcomes.Unsupported)]
    [InlineData(DeviceOutcome.Unreachable, DeviceOutcomes.Unreachable)]
    [InlineData(DeviceOutcome.Error, DeviceOutcomes.Error)]
    public void Label_Outcome_MapsToConstant(DeviceOutcome outcome, string expected) => Assert.Equal(expected, DeviceOutcomes.Label(outcome));

    [Fact]
    public void Label_DeviceResultError_ReturnsErrorConstant() {
        var result = DeviceResult.Error("x");
        Assert.Equal("error", DeviceOutcomes.Label(result));
    }
}
