using EggIncognito.Services;
using Ei;
using Microsoft.Extensions.Logging.Abstractions;

namespace EggIncognito.Tests;

public sealed class EndpointStoreTests : IDisposable {
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    public EndpointStoreTests() {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose() {
        Directory.Delete(_tempDir, true);
        GC.SuppressFinalize(this);
    }

    private EndpointStore CreateStore() =>
        new(new FileEndpointSource(_tempDir), null, NullLogger<EndpointStore>.Instance);

    private void WriteEndpoint(string relativePath, string json) {
        string full = Path.Combine(_tempDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, json);
    }

    [Fact]
    public void ReturnsDefaultInstanceWhenNoEndpoint() {
        var store = CreateStore();
        var result = store.Get<AuthenticatedMessage>("ei/first_contact_secure");
        Assert.NotNull(result);
        Assert.IsType<AuthenticatedMessage>(result);
    }

    [Fact]
    public void ReturnsEndpointWhenDefaultExists() {
        WriteEndpoint("default/ei/first_contact_secure.json", "{}");
        var store = CreateStore();
        var result = store.Get<AuthenticatedMessage>("ei/first_contact_secure");
        Assert.NotNull(result);
    }

    [Fact]
    public void PrefersEidEndpointOverDefault() {
        WriteEndpoint("default/ei/get_periodicals.json", "{}");
        WriteEndpoint("eids/EI0000000000000001/ei/get_periodicals.json", "{}");

        var store = CreateStore();

        var resultDefault = store.Get<PeriodicalsResponse>("ei/get_periodicals");
        Assert.NotNull(resultDefault);

        var resultEid = store.Get<PeriodicalsResponse>("ei/get_periodicals", "EI0000000000000001");
        Assert.NotNull(resultEid);
    }

    [Fact]
    public void FallsBackToDefaultWhenEidEndpointMissing() {
        WriteEndpoint("default/ei/get_periodicals.json", "{}");

        var store = CreateStore();
        var result = store.Get<PeriodicalsResponse>("ei/get_periodicals", "EI_NONEXISTENT");
        Assert.NotNull(result);
    }

    [Fact]
    public void UsesGroupedPathForLookup() {
        WriteEndpoint("default/ei_afx/launch_mission.json", "{}");

        var store = CreateStore();
        var result = store.Get<MissionResponse>("ei_afx/launch_mission");
        Assert.NotNull(result);
    }

    [Fact]
    public void DoesNotThrowWhenEndpointsDirMissing() {
        var store = new EndpointStore(
            new FileEndpointSource(Path.Combine(_tempDir, "does_not_exist")),
            null,
            NullLogger<EndpointStore>.Instance);
        var result = store.Get<AuthenticatedMessage>("ei/any");
        Assert.NotNull(result);
    }
}
