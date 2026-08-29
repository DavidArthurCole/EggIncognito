using Google.Protobuf;

namespace EggIncognito.Core.Services;

public interface IEndpointStore {
    TRes Fetch<TRes>(string path, string? eid = null) where TRes : IMessage<TRes>, new();

    IMessage Fetch(Type messageType, string path, string? eid = null);
}
