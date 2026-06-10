using Google.Protobuf;

namespace EggIncognito.Services;

public interface IEndpointStore
{
    TRes Get<TRes>(string path, string? eid = null) where TRes : IMessage<TRes>, new();
    // Runtime-typed lookup for the dynamic controller, proto type known only at runtime.
    IMessage Get(System.Type messageType, string path, string? eid = null);
}
