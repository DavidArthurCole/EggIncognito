using EggIdentity.UI;
using EggIncognito.Models.Events;

namespace EggIncognito.Services.Events;

public static class EventCalendarLayout {
    public const double DayGapFraction = 0.05;

    private const double MinWidthFraction = 0.006;

    public static double GapPercent(DateTimeOffset start, DateTimeOffset end) =>
        100.0 / Math.Max(1, (end - start).TotalDays) * DayGapFraction;

    public static (DateTimeOffset Start, DateTimeOffset End) Window(DateTimeOffset center, EventCalendarZoom zoom) {
        var local = center.ToLocalTime().DateTime;
        if (zoom == EventCalendarZoom.Week) {
            var weekStart = WeekStart(local);
            return (ToOffset(weekStart), ToOffset(weekStart.AddDays(7)));
        }

        var monthStart = new DateTime(local.Year, local.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);
        return (ToOffset(monthStart), ToOffset(monthStart.AddMonths(1)));
    }

    public static DateTimeOffset ToOffset(DateTime local) {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, TimeZoneInfo.Local.GetUtcOffset(unspecified));
    }

    public static IReadOnlyList<EventCalendarRow> Rows(
        IReadOnlyList<CalendarItem> items,
        DateTimeOffset visibleStart,
        DateTimeOffset visibleEnd,
        EventCalendarZoom zoom,
        DateTimeOffset now) {
        int? primaryMonth = zoom == EventCalendarZoom.Month ? visibleStart.ToLocalTime().Month : null;
        return RowSpans(visibleStart, visibleEnd, zoom)
            .Select(span => BuildRow(items, span.Start, span.End, now, primaryMonth))
            .ToList();
    }

    private static List<(DateTimeOffset Start, DateTimeOffset End)> RowSpans(
        DateTimeOffset visibleStart, DateTimeOffset visibleEnd, EventCalendarZoom zoom) {
        if (zoom == EventCalendarZoom.Week) return [(visibleStart, visibleEnd)];
        var spans = new List<(DateTimeOffset Start, DateTimeOffset End)>();
        for (var day = WeekStart(visibleStart.ToLocalTime().DateTime);
             ToOffset(day) < visibleEnd;
             day = day.AddDays(7)) {
            spans.Add((ToOffset(day), ToOffset(day.AddDays(7))));
        }

        return spans;
    }

    private static EventCalendarRow BuildRow(
        IReadOnlyList<CalendarItem> items,
        DateTimeOffset start,
        DateTimeOffset end,
        DateTimeOffset now,
        int? primaryMonth) {
        double? nowPercent = now > start && now < end
            ? (now - start).TotalSeconds / (end - start).TotalSeconds * 100
            : null;
        return new EventCalendarRow(start, end, DayCells(start, end, primaryMonth), Lanes(Bars(items, start, end, now)), nowPercent);
    }

    private static List<EventCalendarCell> DayCells(DateTimeOffset start, DateTimeOffset end, int? primaryMonth) {
        var cells = new List<EventCalendarCell>();
        double span = (end - start).TotalSeconds;
        if (span <= 0) return cells;
        for (var day = start.ToLocalTime().DateTime.Date; ToOffset(day) < end; day = day.AddDays(1)) {
            double left = Math.Max(0, (ToOffset(day) - start).TotalSeconds / span * 100);
            cells.Add(new EventCalendarCell(left, day, primaryMonth is { } month && day.Month != month));
        }

        return cells;
    }

    private static List<EventCalendarBar> Bars(
        IReadOnlyList<CalendarItem> items, DateTimeOffset start, DateTimeOffset end, DateTimeOffset now) {
        double windowStart = UnixSeconds.FromTime(start);
        double windowEnd = UnixSeconds.FromTime(end);
        double nowUnix = UnixSeconds.FromTime(now);
        double span = windowEnd - windowStart;
        var hits = items
            .Where(i => i.End > windowStart && i.Start < windowEnd)
            .OrderBy(i => i.Start)
            .ThenByDescending(i => i.End - i.Start)
            .ThenBy(i => i.Key, StringComparer.Ordinal)
            .ToList();
        double laneGap = DayGapFraction / Math.Max(1, (end - start).TotalDays);
        var laneRights = new List<double>();
        var bars = new List<EventCalendarBar>(hits.Count);
        foreach (var item in hits) {
            var (left, width) = Clip(item.Start, item.End, windowStart, span);
            bool past = item.End <= nowUnix;
            bars.Add(new EventCalendarBar(
                item,
                AssignLane(laneRights, left, left + width, laneGap),
                left * 100,
                width * 100,
                !past && item.Start <= nowUnix,
                past,
                item.Start < windowStart,
                item.End > windowEnd));
        }

        return bars;
    }

    private static List<IReadOnlyList<EventCalendarBar>> Lanes(List<EventCalendarBar> bars) {
        var lanes = new List<IReadOnlyList<EventCalendarBar>>();
        foreach (var lane in bars.GroupBy(b => b.Lane).OrderBy(g => g.Key)) lanes.Add([.. lane]);
        return lanes;
    }

    private static int AssignLane(List<double> laneRights, double left, double right, double gap) {
        return CalendarLanePacker.AssignLane(laneRights, left, right, -gap);
    }

    private static (double Left, double Width) Clip(double startUnix, double endUnix, double windowStart, double span) {
        if (span <= 0) return (0, 1);
        double left = Math.Max(0, (startUnix - windowStart) / span);
        double right = Math.Min(1, (endUnix - windowStart) / span);
        double width = Math.Max(0, right - left);
        if (width >= MinWidthFraction) return (left, width);
        width = Math.Min(MinWidthFraction, 1);
        return (Math.Min(left, 1 - width), width);
    }

    private static DateTime WeekStart(DateTime local) =>
        CalendarGridAnchor.WeekStartDate(DateOnly.FromDateTime(local)).ToDateTime(TimeOnly.MinValue);
}
