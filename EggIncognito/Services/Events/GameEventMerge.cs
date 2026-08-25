using EggIncognito.Data.Models;
using EggIncognito.Models.Events;

namespace EggIncognito.Services.Events;

public static class GameEventMerge {
    public static readonly TimeSpan Window = TimeSpan.FromHours(48);

    public static bool SameOccurrence(GameEvent row, GameEventObservation obs) =>
        row.EventId == obs.EventId && (row.StartTime - obs.Start).Duration() <= Window;

    public static GameEvent Create(GameEventObservation obs) => new() {
        EventId = obs.EventId,
        EventType = obs.EventType,
        Message = obs.Message,
        Multiplier = obs.Multiplier,
        Ultra = obs.Ultra,
        StartTime = obs.Start,
        EndTime = obs.End,
        Source = obs.Source,
        FirstSeenAt = obs.SeenAt,
        LastSeenAt = obs.SeenAt
    };

    public static bool Apply(GameEvent row, GameEventObservation obs) {
        if (obs.Source == GameEventSources.Carpet && row.Source == GameEventSources.Device) return false;
        bool changed = false;
        if (obs.Start < row.StartTime) {
            row.StartTime = obs.Start;
            changed = true;
        }
        if (row.EndTime != obs.End) {
            row.EndTime = obs.End;
            changed = true;
        }
        if (obs.Source == GameEventSources.Device) {
            if (row.Source != GameEventSources.Device) {
                row.Source = GameEventSources.Device;
                changed = true;
            }
            if (row.EventType != obs.EventType || row.Message != obs.Message ||
                row.Multiplier != obs.Multiplier || row.Ultra != obs.Ultra) {
                row.EventType = obs.EventType;
                row.Message = obs.Message;
                row.Multiplier = obs.Multiplier;
                row.Ultra = obs.Ultra;
                changed = true;
            }
            if (row.FirstSeenAt is null || obs.SeenAt < row.FirstSeenAt) {
                row.FirstSeenAt = obs.SeenAt;
                changed = true;
            }
            if (row.LastSeenAt is null || obs.SeenAt > row.LastSeenAt) {
                row.LastSeenAt = obs.SeenAt;
                changed = true;
            }
        }
        return changed;
    }
}
