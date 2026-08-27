namespace EggIncognito.Services.Auth;

public static class LocalIdentityGate {
    public const string EnabledKey = "Auth:LocalIdentity:Enabled";
    public const string RoleKey = "Auth:LocalIdentity:Role";
    public const string SupporterKey = "Auth:LocalIdentity:Supporter";
    public const string RequiredEnvironment = "Staging";
    public const string RefusedEnvironment = "Production";

    public static bool IsOn(string environmentName, AppMode mode, IConfiguration config, bool identityConfigured) =>
        Requested(config)
        && !identityConfigured
        && string.Equals(environmentName, RequiredEnvironment, StringComparison.OrdinalIgnoreCase)
        && mode == AppMode.Local;

    public static void Guard(string environmentName, AppMode mode, IConfiguration config) {
        if (!Requested(config)) return;

        if (string.Equals(environmentName, RefusedEnvironment, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"{EnabledKey} is set but the environment is {RefusedEnvironment}. The local identity only loads in a " +
                $"{RequiredEnvironment} instance running AppMode=Local. Unset {EnabledKey} or fix ASPNETCORE_ENVIRONMENT.");

        if (mode == AppMode.Hosted)
            throw new InvalidOperationException(
                $"{EnabledKey} is set but AppMode is Hosted. The local identity only loads in a {RequiredEnvironment} " +
                $"instance running AppMode=Local. Unset {EnabledKey} or fix AppMode.");
    }

    private static bool Requested(IConfiguration config) => config.GetValue(EnabledKey, false);
}
