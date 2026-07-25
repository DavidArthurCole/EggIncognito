namespace EggIncognito.Services.Feed;

public static class FeedTrigger {
    public static bool Matches(string trigger, bool created, bool protoChanged,
        IReadOnlyList<string> subPlatforms, string evtPlatform) => subPlatforms.Contains(evtPlatform) &&
                                                                   (trigger == "proto_changed"
                                                                       ? protoChanged
                                                                       : created);
}
