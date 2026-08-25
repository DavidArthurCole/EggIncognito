namespace EggIncognito.Services.Events;

public static class UnixSeconds {
    public static DateTimeOffset ToTime(double seconds) =>
        DateTimeOffset.UnixEpoch.AddSeconds(seconds);

    public static double FromTime(DateTimeOffset time) =>
        (time - DateTimeOffset.UnixEpoch).TotalSeconds;

    public static bool IsValid(double seconds) =>
        double.IsFinite(seconds) && seconds >= -62135596800d && seconds <= 253402300799d;
}
