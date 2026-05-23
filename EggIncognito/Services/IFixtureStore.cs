using Google.Protobuf;

namespace EggIncognito.Services;

public interface IFixtureStore
{
    TRes Get<TRes>(string path, string? eid = null) where TRes : IMessage<TRes>, new();
}
