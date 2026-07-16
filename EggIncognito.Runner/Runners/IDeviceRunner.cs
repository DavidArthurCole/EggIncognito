namespace EggIncognito.Runner.Runners;
public interface IDeviceRunner
{
    string Platform { get; }
    RunOutcome RunOnce(bool force);
}

public sealed record RunOutcome(bool Emitted, string? Build, string? ProtoSha, string Detail);
