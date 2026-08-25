namespace EggIncognito.Core.Services.Devices;

public static class DeviceOutcomes {
    public const string Ok = "ok";
    public const string Unsupported = "unsupported";
    public const string Unreachable = "unreachable";
    public const string Error = "error";

    public static string Label(DeviceOutcome outcome) => outcome switch {
        DeviceOutcome.Ok => Ok,
        DeviceOutcome.Unsupported => Unsupported,
        DeviceOutcome.Unreachable => Unreachable,
        DeviceOutcome.Error => Error,
        _ => Error
    };

    public static string Label(DeviceResult result) => Label(result.Outcome);
}
