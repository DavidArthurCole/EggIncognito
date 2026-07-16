namespace EggIncognito.Services.Feed;
public static class FeedTrigger
{
    public static bool Matches(string trigger, bool created, bool protoChanged,
        IReadOnlyList<string> subPlatforms, string evtPlatform)
    {
        if (!subPlatforms.Contains(evtPlatform)) return false;
        return trigger == "proto_changed" ? protoChanged : created;
    }
}
