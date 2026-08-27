using System.Globalization;

namespace EggIncognito.Services.Events;

public static class EventCountdown {
    public static string Describe(double startUnix, double endUnix, DateTimeOffset now) {
        var start = UnixSeconds.ToTime(startUnix);
        var end = UnixSeconds.ToTime(endUnix);
        if (now < start) return $"Starts in {Span(start - now)}";
        if (now < end) return $"Ends in {Span(end - now)}";
        return $"Ended {Span(now - end)} ago";
    }

    public static string Span(TimeSpan delta) {
        if (delta < TimeSpan.Zero) delta = TimeSpan.Zero;
        if (delta.TotalMinutes < 1) return "under a minute";
        if (delta.TotalHours < 1) return Unit((int)delta.TotalMinutes, "m");
        if (delta.TotalDays < 1) return Pair((int)delta.TotalHours, "h", delta.Minutes, "m");
        if (delta.TotalDays < 30) return Pair((int)delta.TotalDays, "d", delta.Hours, "h");
        return Unit((int)(delta.TotalDays / 30), "mo");
    }

    private static string Pair(int major, string majorUnit, int minor, string minorUnit) =>
        minor == 0 ? Unit(major, majorUnit) : Unit(major, majorUnit) + " " + Unit(minor, minorUnit);

    private static string Unit(int value, string unit) =>
        value.ToString(CultureInfo.InvariantCulture) + unit;
}
