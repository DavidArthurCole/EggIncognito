using System.Text;
using Google.Protobuf;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EggIncognito.Services;

public sealed class EndpointStore(
    IEndpointSource fileSource,
    IServiceScopeFactory? scopeFactory,
    ILogger<EndpointStore> logger) : IEndpointStore {
    public TRes Fetch<TRes>(string path, string? eid = null) where TRes : IMessage<TRes>, new() {
        byte[]? bytes = LookupBytes(path, eid);
        return bytes is null ? new TRes() : JsonParser.Default.Parse<TRes>(Encoding.UTF8.GetString(bytes));
    }


    public IMessage Fetch(Type messageType, string path, string? eid = null) {
        var instance = (IMessage)Activator.CreateInstance(messageType)!;
        byte[]? bytes = LookupBytes(path, eid);
        return bytes is null ? instance : JsonParser.Default.Parse(Encoding.UTF8.GetString(bytes), instance.Descriptor);
    }

    internal byte[]? LookupBytes(string path, string? eid) {
        if (scopeFactory is not null) {
            try {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetService<DbEndpointSourceMarker>()?.Source;
                byte[]? hit = db?.Lookup(path, eid);
                if (hit is not null) return hit;
            } catch (Exception ex) {
                logger.LogDbEndpointLookupFailed(ex, path, eid);
            }
        }

        return fileSource.Lookup(path, eid);
    }
}

public sealed class DbEndpointSourceMarker(IEndpointSource source) {
    public IEndpointSource Source => source;
}

internal static partial class EndpointStoreLog {
    [LoggerMessage(EventId = 1, Level = LogLevel.Warning,
        Message = "DB endpoint lookup failed for {Path} (eid {Eid}); using file default")]
    internal static partial void LogDbEndpointLookupFailed(this ILogger logger, Exception ex, string path, string? eid);
}
