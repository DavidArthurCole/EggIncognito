using EggIncognito.Models.Events;
using Ei;

namespace EggIncognito.Services.Events;

public static class GameEventMapper {
    public static IReadOnlyList<GameEventObservation> FromPeriodicals(
        PeriodicalsResponse response, DateTimeOffset seenAt) {
        if (response.Events is null) return [];
        var list = new List<GameEventObservation>();
        foreach (var e in response.Events.Events) {
            if (string.IsNullOrEmpty(e.Identifier) || e.StartTime <= 0 || e.Duration <= 0) continue;
            if (!UnixSeconds.IsValid(e.StartTime) || !double.IsFinite(e.Duration) ||
                !UnixSeconds.IsValid(e.StartTime + e.Duration)) {
                continue;
            }
            var start = UnixSeconds.ToTime(e.StartTime);
            list.Add(new GameEventObservation(
                e.Identifier, e.Type, e.Subtitle, e.Multiplier, e.CcOnly,
                start, start.AddSeconds(e.Duration), GameEventSources.Device, seenAt));
        }
        return list;
    }

    public static IReadOnlyList<GameEventObservation> FromCarpet(IEnumerable<CarpetEvent> events) {
        var list = new List<GameEventObservation>();
        foreach (var e in events) {
            if (string.IsNullOrEmpty(e.Id) || e.StartTimestamp <= 0 || e.EndTimestamp <= e.StartTimestamp) continue;
            if (!UnixSeconds.IsValid(e.StartTimestamp) || !UnixSeconds.IsValid(e.EndTimestamp)) continue;
            list.Add(new GameEventObservation(
                e.Id, e.Type ?? "", e.Message ?? "", e.Multiplier, e.Ultra,
                UnixSeconds.ToTime(e.StartTimestamp), UnixSeconds.ToTime(e.EndTimestamp),
                GameEventSources.Carpet, null));
        }
        return list;
    }
}
