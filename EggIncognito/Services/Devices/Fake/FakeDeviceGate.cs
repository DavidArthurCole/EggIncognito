namespace EggIncognito.Services.Devices.Fake;

public static class FakeDeviceGate {
    public const string EnabledKey = "Devices:Fake:Enabled";
    public const string RequiredEnvironment = "Staging";
    public const string RefusedEnvironment = "Production";

    public static bool IsOn(string environmentName, AppMode mode, IConfiguration config) =>
        Requested(config)
        && string.Equals(environmentName, RequiredEnvironment, StringComparison.OrdinalIgnoreCase)
        && mode == AppMode.Local;

    public static void Guard(string environmentName, AppMode mode, IConfiguration config) {
        if (!Requested(config)) return;

        if (string.Equals(environmentName, RefusedEnvironment, StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidOperationException(
                $"{EnabledKey} is set but the environment is {RefusedEnvironment}. Fake devices only load in a " +
                $"{RequiredEnvironment} instance running AppMode=Local. Unset {EnabledKey} or fix ASPNETCORE_ENVIRONMENT.");
        }

        if (mode == AppMode.Hosted) {
            throw new InvalidOperationException(
                $"{EnabledKey} is set but AppMode is Hosted. Fake devices only load in a {RequiredEnvironment} " +
                $"instance running AppMode=Local. Unset {EnabledKey} or fix AppMode.");
        }
    }

    private static bool Requested(IConfiguration config) => config.GetValue(EnabledKey, false);
}
