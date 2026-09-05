using EggIncognito.Services.Predictions;

namespace EggIncognito.Tests.Predictions;

internal static class EventTemplate {
    public const double Day = 86400d;
    public static readonly DateOnly End = new(2026, 9, 5);
    public static double AsOf => NoonEastern.SlotTime(End) + 3600;

    private static readonly DateOnly WeekAnchor = new(2021, 1, 3);
    private static readonly DateOnly UltraAnchor = new(2023, 8, 1);

    private static readonly string[] TuesdayPool =
        ["hab-sale", "vehicle-sale", "gift-boost", "shell-sale", "hab-sale", "mission-fuel", "boost-duration"];

    private static readonly string[] WednesdayPool =
        ["vehicle-sale", "boost-duration", "shell-sale", "gift-boost", "mission-fuel", "vehicle-sale", "hab-sale"];

    private static readonly string[] ThursdayPool =
        ["drone-boost", "gift-boost", "drone-boost", "boost-duration", "drone-boost", "shell-sale", "mission-fuel"];

    private static readonly string[] UltraPool =
        ["hab-sale", "vehicle-sale", "drone-boost", "gift-boost", "boost-duration", "mission-fuel", "shell-sale"];

    public static List<EventRow> Build(
        DateOnly first, DateOnly last, string? stopped = null, DateOnly? stoppedFrom = null) {
        var rows = new List<EventRow>();
        for (var day = first; day <= last; day = day.AddDays(1)) {
            foreach (var (type, days) in Standard(day)) {
                if (Stopped(type, day, stopped, stoppedFrom)) continue;
                rows.Add(Row(type, false, day, days));
            }
            int offset = day.DayNumber - UltraAnchor.DayNumber;
            if (offset < 0 || offset % 2 != 0) continue;
            rows.Add(Row(UltraPool[offset / 2 % UltraPool.Length], true, day, 1));
        }
        return rows;
    }

    private static bool Stopped(string type, DateOnly day, string? stopped, DateOnly? stoppedFrom) =>
        stopped is not null
        && string.Equals(type, stopped, StringComparison.Ordinal)
        && stoppedFrom is { } from
        && day >= from;

    private static IEnumerable<(string Type, double Days)> Standard(DateOnly day) {
        int week = (day.DayNumber - WeekAnchor.DayNumber) / 7;
        switch (day.DayOfWeek) {
            case DayOfWeek.Sunday:
                yield return (week % 2 == 0 ? "crafting-sale" : "epic-research-sale", 1);
                if (week % 4 == 0) yield return ("mission-capacity", 2);
                break;
            case DayOfWeek.Monday:
                yield return ("earnings-boost", 1);
                break;
            case DayOfWeek.Tuesday:
                yield return (TuesdayPool[week % TuesdayPool.Length], 1);
                break;
            case DayOfWeek.Wednesday:
                yield return ("piggy-boost", 1);
                yield return (WednesdayPool[week % WednesdayPool.Length], 1);
                break;
            case DayOfWeek.Thursday:
                yield return (ThursdayPool[week % ThursdayPool.Length], 1);
                break;
            case DayOfWeek.Friday:
                yield return ("research-sale", 1);
                break;
            default:
                yield return ("prestige-boost", 1);
                yield return ("piggy-boost", 2);
                break;
        }
    }

    private static EventRow Row(string type, bool ultra, DateOnly day, double days) {
        double start = NoonEastern.SlotTime(day);
        return new EventRow(type, ultra, start, start + days * Day);
    }
}
