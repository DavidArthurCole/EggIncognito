using EggIncognito.Runner.Extract;
using EggIncognito.Runner.State;
using Xunit;

namespace EggIncognito.Runner.Tests;

public class ClientVersionTests
{
    [Fact]
    public void Reader_NullPrev_ReturnsNull_WithoutRunningTool()
    {
        // No anchor means no disambiguation; the reader returns null without shelling python.
        var reader = new LibegincClientVersionReader("repo", "python-does-not-exist");
        Assert.Null(reader.Read("/x/arm.apk", null));
    }

    [Fact]
    public void State_SeedThenSave_RoundTrips()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cv-{Guid.NewGuid():N}");
        try
        {
            var s = new ClientVersionState(path, seed: 71);
            Assert.Equal(71, s.Last());
            s.Save(72);
            Assert.Equal(72, new ClientVersionState(path, seed: null).Last());
        }
        finally { try { File.Delete(path); } catch { } }
    }

    [Fact]
    public void State_NoFileNoSeed_IsNull()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cv-{Guid.NewGuid():N}");
        Assert.Null(new ClientVersionState(path, seed: null).Last());
    }
}
