using EggIncognito.Services.Events;

namespace EggIncognito.Services.Predictions;

public static class NoonEastern {
    public static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
    private static readonly TimeOnly Noon = new(12, 0);

    public static double SlotTime(DateOnly day) =>
        UnixSeconds.FromTime(new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(Noon), Zone)));

    public static DateOnly LocalDate(double unix) => LocalDate(UnixSeconds.ToTime(unix));

    public static DateOnly LocalDate(DateTimeOffset time) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time, Zone).DateTime);
}
