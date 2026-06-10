using System.Text;
using EggIncognito.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace EggIncognito.Tests;

public class EndpointStoreLayeringTests
{
    private sealed class FakeSource(Dictionary<string, string> map, int prio) : IEndpointSource
    {
        public int Priority => prio;
        public byte[]? Lookup(string path, string? eid)
            => map.TryGetValue(eid is null ? path : $"{eid}:{path}", out var v) ? Encoding.UTF8.GetBytes(v) : null;
    }

    // Build a store with a file source and an optional DB source resolved via a scope factory + marker.
    private static EndpointStore Store(IEndpointSource file, IEndpointSource? db)
    {
        IServiceScopeFactory? factory = null;
        if (db is not null)
        {
            var services = new ServiceCollection();
            services.AddScoped(_ => new DbEndpointSourceMarker(db));
            factory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        }
        return new EndpointStore(file, factory, NullLogger<EndpointStore>.Instance);
    }

    [Fact]
    public void FileOnly_BehavesAsBefore()
    {
        var file = new FakeSource(new() { ["ei/x"] = "{}" }, 0);
        var store = Store(file, db: null);
        var msg = store.Get<Ei.PeriodicalsResponse>("ei/x");
        Assert.NotNull(msg);
    }

    [Fact]
    public void Db_Overrides_File_ForSameKey()
    {
        var file = new FakeSource(new() { ["ei/x"] = "{}" }, 0);
        var db = new FakeSource(new() { ["ei/x"] = "{\"userId\":\"FROM_DB\"}" }, 100);
        var store = Store(file, db);
        var msg = store.Get<Ei.AuthenticatedMessage>("ei/x");
        Assert.Equal("FROM_DB", msg.UserId);
    }

    [Fact]
    public void NonGeneric_Get_ByType()
    {
        var file = new FakeSource(new() { ["ei/x"] = "{}" }, 0);
        var store = Store(file, db: null);
        var msg = store.Get(typeof(Ei.AuthenticatedMessage), "ei/x", null);
        Assert.IsType<Ei.AuthenticatedMessage>(msg);
    }

    [Fact]
    public void NonGeneric_Get_AppliesDbOverlay()
    {
        var file = new FakeSource(new() { ["ei/x"] = "{}" }, 0);
        var db = new FakeSource(new() { ["ei/x"] = "{\"userId\":\"FROM_DB\"}" }, 100);
        var store = Store(file, db);
        var msg = (Ei.AuthenticatedMessage)store.Get(typeof(Ei.AuthenticatedMessage), "ei/x", null);
        Assert.Equal("FROM_DB", msg.UserId);
    }

    [Fact]
    public void Miss_ReturnsDefaultInstance()
    {
        var file = new FakeSource(new(), 0);
        var store = Store(file, db: null);
        Assert.NotNull(store.Get<Ei.PeriodicalsResponse>("ei/none"));
    }
}
