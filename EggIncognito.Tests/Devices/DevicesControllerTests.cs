using EggIncognito.Controllers;
using EggIncognito.Data.Models;
using EggIncognito.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EggIncognito.Tests.Devices;

public class DevicesControllerTests
{
    sealed class FakeUser(UserRole role) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? DiscordId => "123";
        public string? Username => "tester";
        public string? Avatar => null;
        public UserRole Role => role;
        public bool IsSupporter => false;
        public bool IsAtLeast(UserRole need) => UserRoles.IsAtLeast(Role, need);
    }

    static DevicesController Make(UserRole role, IServiceProvider sp) =>
        new(new FakeUser(role), sp);

    [Fact]
    public async Task Refresh_NonAdmin_403()
    {
        var sp = new ServiceCollection().BuildServiceProvider(); // no DB registered
        var c = Make(UserRole.Contributor, sp);
        var r = await c.Refresh("frame-android");
        var sc = Assert.IsType<ObjectResult>(r);
        Assert.Equal(403, sc.StatusCode);
    }

    [Fact]
    public async Task Refresh_Admin_NoDb_503()
    {
        var sp = new ServiceCollection().BuildServiceProvider(); // no IDeviceStatusStore
        var c = Make(UserRole.Admin, sp);
        var r = await c.Refresh("frame-android");
        var sc = Assert.IsType<ObjectResult>(r);
        Assert.Equal(503, sc.StatusCode);
    }

    [Fact]
    public async Task Status_NoDb_ReturnsEmptyArray()
    {
        var sp = new ServiceCollection().BuildServiceProvider();
        var c = Make(UserRole.Viewer, sp);
        var r = await c.Status();
        var ok = Assert.IsType<OkObjectResult>(r);
        Assert.NotNull(ok.Value);
    }
}
