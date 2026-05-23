using EggIncognito.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace EggIncognito.Tests;

public sealed class FixtureStoreTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    public FixtureStoreTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        Directory.Delete(_tempDir, recursive: true);
        GC.SuppressFinalize(this);
    }

    private FixtureStore CreateStore() =>
        new(_tempDir, NullLogger<FixtureStore>.Instance);

    private void WriteFixture(string relativePath, string json)
    {
        var full = Path.Combine(_tempDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, json);
    }

    [Fact]
    public void ReturnsDefaultInstanceWhenNoFixture()
    {
        var store = CreateStore();
        var result = store.Get<Ei.AuthenticatedMessage>("ei/first_contact_secure");
        Assert.NotNull(result);
        Assert.IsType<Ei.AuthenticatedMessage>(result);
    }

    [Fact]
    public void ReturnsFixtureWhenDefaultExists()
    {
        WriteFixture("default/ei_first_contact_secure.json", "{}");
        var store = CreateStore();
        var result = store.Get<Ei.AuthenticatedMessage>("ei/first_contact_secure");
        Assert.NotNull(result);
    }

    [Fact]
    public void PrefersEidFixtureOverDefault()
    {
        WriteFixture("default/ei_get_periodicals.json", "{}");
        WriteFixture("eids/EI0000000000000001/ei_get_periodicals.json", "{}");

        var store = CreateStore();

        var resultDefault = store.Get<Ei.PeriodicalsResponse>("ei/get_periodicals", null);
        Assert.NotNull(resultDefault);

        var resultEid = store.Get<Ei.PeriodicalsResponse>("ei/get_periodicals", "EI0000000000000001");
        Assert.NotNull(resultEid);
    }

    [Fact]
    public void FallsBackToDefaultWhenEidFixtureMissing()
    {
        WriteFixture("default/ei_get_periodicals.json", "{}");

        var store = CreateStore();
        var result = store.Get<Ei.PeriodicalsResponse>("ei/get_periodicals", "EI_NONEXISTENT");
        Assert.NotNull(result);
    }

    [Fact]
    public void SlugifiesPathCorrectly()
    {
        WriteFixture("default/ei_afx_launch_mission.json", "{}");

        var store = CreateStore();
        var result = store.Get<Ei.MissionResponse>("ei_afx/launch_mission");
        Assert.NotNull(result);
    }

    [Fact]
    public void DoesNotThrowWhenFixturesDirMissing()
    {
        var store = new FixtureStore(
            Path.Combine(_tempDir, "does_not_exist"),
            NullLogger<FixtureStore>.Instance);
        var result = store.Get<Ei.AuthenticatedMessage>("ei/any");
        Assert.NotNull(result);
    }
}
