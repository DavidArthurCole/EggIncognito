using EggIncognito.Runner.Extract;
using EggIncognito.Runner.State;
using Xunit;

namespace EggIncognito.Runner.Tests;

public class ClientVersionTests {
    [Fact]
    public void Reader_NullPrev_ReturnsNull_WithoutRunningTool() {
        var reader = new LibegincClientVersionReader();
        Assert.Null(reader.Read("/x/arm.apk", null));
    }

    [Fact]
    public void State_SeedThenSave_RoundTrips() {
        using var tmp = new TempDir();
        string path = tmp.Combine("cv");
        var s = new ClientVersionState(path, seed: 71);
        Assert.Equal(71, s.Last());
        s.Save(72);
        Assert.Equal(72, new ClientVersionState(path, seed: null).Last());
    }

    [Fact]
    public void State_NoFileNoSeed_IsNull() {
        using var tmp = new TempDir();
        Assert.Null(new ClientVersionState(tmp.Combine("cv"), seed: null).Last());
    }
}
