using EggIncognito.Controllers;
using EggIncognito.Data.Models;
using EggIncognito.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace EggIncognito.Tests;

public class StoredEndpointRoleTests
{
    private sealed class FakeUser(UserRole role) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public Guid? UserId => null;
        public string? DiscordId => "tester";
        public string? Username => "tester";
        public string? Avatar => null;
        public string? AvatarUrl => null;
        public UserRole Role => role;
        public bool IsSupporter => false;
        public bool IsAtLeast(UserRole need) => UserRoles.IsAtLeast(role, need);
    }

   
   
    private sealed class EmptyServices : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private static StoredEndpointController Controller(UserRole role)
        => new(new FakeUser(role), new EmptyServices());

    private sealed class FakeRoutes : IRouteCatalog
    {
        public IReadOnlyList<RouteInfo> All() => [];
        public RouteInfo? Get(string path) => new(path, null, "PeriodicalsResponse", false, false, null, false, false);
    }

    [Fact]
    public async Task Viewer_Upsert_Is403()
    {
        var r = await Controller(UserRole.Viewer).UpsertEndpointAsync(
            new StoredEndpointController.UpsertEndpoint("ei/x", null, "{}", "PeriodicalsResponse"), new FakeRoutes());
        Assert.Equal(403, ((IStatusCodeActionResult)r).StatusCode);
    }

    [Fact]
    public async Task Contributor_PassesGate_Then503NoDb()
    {
        var r = await Controller(UserRole.Contributor).UpsertEndpointAsync(
            new StoredEndpointController.UpsertEndpoint("ei/x", null, "{}", "PeriodicalsResponse"), new FakeRoutes());
        Assert.Equal(503, ((IStatusCodeActionResult)r).StatusCode);
    }
}
