using System.Text;
using Google.Protobuf;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EggIncognito.Services;

// Coordinates endpoint lookup across sources. The file source (singleton) is always present; a DB
// overlay source (scoped, so resolved per-lookup via the scope factory) is consulted FIRST when the
// app is configured with a database, so a stored row overrides the file default for the same key.
// With no scope factory the store is exactly the former file-only behavior.
public sealed class EndpointStore : IEndpointStore
{
    private readonly IEndpointSource _fileSource;
    private readonly IServiceScopeFactory? _scopeFactory;
    private readonly ILogger<EndpointStore> _logger;

    public EndpointStore(IEndpointSource fileSource, IServiceScopeFactory? scopeFactory, ILogger<EndpointStore> logger)
    {
        _fileSource = fileSource;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public TRes Get<TRes>(string path, string? eid = null) where TRes : IMessage<TRes>, new()
    {
        var bytes = LookupBytes(path, eid);
        return bytes is null ? new TRes() : JsonParser.Default.Parse<TRes>(Encoding.UTF8.GetString(bytes));
    }

    // Runtime-typed lookup for the dynamic controller (it only knows the proto type at runtime).
    public IMessage Get(System.Type messageType, string path, string? eid = null)
    {
        var instance = (IMessage)Activator.CreateInstance(messageType)!;
        var bytes = LookupBytes(path, eid);
        if (bytes is null) return instance;
        // JsonParser.Parse(string, MessageDescriptor) returns a populated IMessage of that type.
        return JsonParser.Default.Parse(Encoding.UTF8.GetString(bytes), instance.Descriptor);
    }

    internal byte[]? LookupBytes(string path, string? eid)
    {
        if (_scopeFactory is not null)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetService<DbEndpointSourceMarker>()?.Source;
                var hit = db?.Lookup(path, eid);
                if (hit is not null) return hit;
            }
            catch (Exception ex)
            {
                // A transient DB error must not fail the request: fall back to the file default.
                _logger.LogWarning(ex, "DB endpoint lookup failed for {Path} (eid {Eid}); using file default", path, eid);
            }
        }
        return _fileSource.Lookup(path, eid);
    }
}

// Lets Core resolve a DB-provided IEndpointSource from a scope without referencing EggIncognito.Data.
// EggIncognito.Data registers a DbEndpointSourceMarker wrapping its scoped DbEndpointSource.
public sealed class DbEndpointSourceMarker(IEndpointSource source)
{
    public IEndpointSource Source => source;
}
