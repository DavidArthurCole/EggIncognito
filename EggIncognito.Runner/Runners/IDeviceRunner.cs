namespace EggIncognito.Runner.Runners;

// force=true ignores saved state and always emits; force=false emits only on an unseen build.
public interface IDeviceRunner
{
    string Platform { get; }
    RunOutcome RunOnce(bool force);
}

public sealed record RunOutcome(bool Emitted, string? Build, string? ProtoSha, string Detail);
