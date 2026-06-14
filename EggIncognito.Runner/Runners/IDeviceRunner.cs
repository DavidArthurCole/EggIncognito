namespace EggIncognito.Runner.Runners;

// One platform's detect+pull+extract+emit cycle. force=true ignores saved state and always emits
// (the re-sync path); force=false emits only on a build the state has not seen. Returns the outcome so
// the trigger listener and the loop can report it.
public interface IDeviceRunner
{
    string Platform { get; }
    RunOutcome RunOnce(bool force);
}

public sealed record RunOutcome(bool Emitted, string? Build, string? ProtoSha, string Detail);
