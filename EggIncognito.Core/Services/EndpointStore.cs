using System.Text;
using Google.Protobuf;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EggIncognito.Services;

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

   
    public IMessage Get(System.Type messageType, string path, string? eid = null)
    {
        var instance = (IMessage)Activator.CreateInstance(messageType)!;
        var bytes = LookupBytes(path, eid);
        if (bytes is null) return instance;
       
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
               
                _logger.LogWarning(ex, "DB endpoint lookup failed for {Path} (eid {Eid}); using file default", path, eid);
            }
        }
        return _fileSource.Lookup(path, eid);
    }
}

public sealed class DbEndpointSourceMarker(IEndpointSource source)
{
    public IEndpointSource Source => source;
}
