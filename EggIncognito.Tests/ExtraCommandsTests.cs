using EggIncognito.Bot;
using EggIncognito.Services;
using Xunit;

namespace EggIncognito.Tests;

public class ExtraCommandsTests
{
    [Fact]
    public void HealthCommand_HasExpectedName()
    {
        var cmd = ExtraCommands.HealthCommand(DateTimeOffset.UtcNow);
        Assert.Equal("health", cmd.Name);
    }

    [Fact]
    public void StatusCommand_HasExpectedName()
    {
        var cmd = ExtraCommands.StatusCommand(new FakeStatusProvider());
        Assert.Equal("status", cmd.Name);
    }

    [Fact]
    public void EndpointsCommand_HasExpectedName()
    {
        var cmd = ExtraCommands.EndpointsCommand(new FakeStatusProvider());
        Assert.Equal("endpoints", cmd.Name);
    }

    [Fact]
    public void ProtoCommand_HasExpectedNameAndAutocompleteHandler()
    {
        var cmd = ExtraCommands.ProtoCommand(new FakeProtoReflection());
        Assert.Equal("proto", cmd.Name);
        Assert.NotNull(cmd.AutocompleteHandler);
    }

    internal sealed class FakeStatusProvider : IStatusProvider
    {
        public StatusSnapshot Build() => new(
            Mode: "Local", CanCapture: true, CanWrite: true, CaptureState: "Idle",
            CaptureRunning: false, FlowsCaptured: 0, DeviceCount: 0, BytesCaptured: 0,
            DbEnabled: false, SigningReady: false, Uptime: TimeSpan.Zero,
            Build: new BuildInfo("0.0.0", "unknown", "unknown", "unknown", "https://example.com"),
            EndpointsOk: 0, EndpointsEmpty: 0, EndpointsMissing: 0);
    }

    internal sealed class FakeProtoReflection : IProtoReflection
    {
        public Google.Protobuf.Reflection.MessageDescriptor? FindMessage(string typeName) => null;
        public Google.Protobuf.MessageParser? FindParser(string typeName) => null;
        public SchemaMessage? Schema(string typeName) => null;
        public IReadOnlyList<string> AllMessageTypeNames() => Array.Empty<string>();
    }
}
