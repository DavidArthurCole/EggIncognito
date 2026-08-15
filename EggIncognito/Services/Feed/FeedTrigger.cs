using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Services.Feed;

public static class FeedTrigger {
    public static bool Matches(
        string trigger, bool created, bool protoChanged, VersionDelta delta, bool flawed,
        IReadOnlyList<string> subPlatforms, string evtPlatform) {
        if (!subPlatforms.Contains(evtPlatform)) return false;

        return trigger switch {
            FeedEventKinds.TriggerVersionUp => delta == VersionDelta.Forward,
            FeedEventKinds.TriggerProtoChanged => protoChanged,
            FeedEventKinds.TriggerSuspect => flawed || delta is VersionDelta.Backfill or VersionDelta.Unknown,
            _ => created
        };
    }
}
