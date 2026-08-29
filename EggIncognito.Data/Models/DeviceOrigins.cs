namespace EggIncognito.Data.Models;

public static class DeviceOrigins {
    public const string Runtime = "runtime";
    public const string Config = "config";
    public const string Virtual = "virtual";

    public static bool IsVirtual(string? origin) =>
        string.Equals(origin, Virtual, StringComparison.Ordinal);
}
