using EggIncognito.Controllers;
using EggIncognito.Services;
using EggIncognito.Services.Devices;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using SyncKit.Contract;

namespace EggIncognito.Tests.Devices;

public class DevicesControllerTests {
    private static DevicesController Make(UserRole role, IServiceProvider sp, IDeviceJobTracker? jobs = null) =>
        new(new FakeUser(role), sp,
            sp.GetService<IServiceScopeFactory>() ?? new ServiceCollection().BuildServiceProvider()
                .GetRequiredService<IServiceScopeFactory>(),
            jobs ?? new DeviceJobTracker(TimeProvider.System));

    [Fact]
    public async Task Refresh_NonAdmin_403() {
        var sp = new ServiceCollection().BuildServiceProvider();
        var c = Make(UserRole.Contributor, sp);
        var r = await c.Refresh("frame-android");
        var sc = Assert.IsType<ObjectResult>(r);
        Assert.Equal(403, sc.StatusCode);
    }

    [Fact]
    public async Task Refresh_Admin_NoDb_503() {
        var sp = new ServiceCollection().BuildServiceProvider();
        var c = Make(UserRole.Admin, sp);
        var r = await c.Refresh("frame-android");
        var sc = Assert.IsType<ObjectResult>(r);
        Assert.Equal(503, sc.StatusCode);
    }

    [Fact]
    public async Task Status_NoDb_ReturnsEmptyArray() {
        var sp = new ServiceCollection().BuildServiceProvider();
        var c = Make(UserRole.Viewer, sp);
        var r = await c.Status();
        var ok = Assert.IsType<OkObjectResult>(r);
        Assert.NotNull(ok.Value);
    }

    [Fact]
    public async Task CheckUpdate_NonAdmin_403() {
        var sp = new ServiceCollection().BuildServiceProvider();
        var c = Make(UserRole.Contributor, sp);
        var r = await c.CheckUpdate("frame-android");
        var sc = Assert.IsType<ObjectResult>(r);
        Assert.Equal(403, sc.StatusCode);
    }

    [Fact]
    public async Task CheckUpdate_Admin_NoDb_503() {
        var sp = new ServiceCollection().BuildServiceProvider();
        var c = Make(UserRole.Admin, sp);
        var r = await c.CheckUpdate("frame-android");
        var sc = Assert.IsType<ObjectResult>(r);
        Assert.Equal(503, sc.StatusCode);
    }

    [Fact]
    public void CheckStatus_NonAdmin_403() {
        var sp = new ServiceCollection().BuildServiceProvider();
        var c = Make(UserRole.Viewer, sp);
        var r = c.CheckStatus("frame-android");
        var sc = Assert.IsType<ObjectResult>(r);
        Assert.Equal(403, sc.StatusCode);
    }

    [Fact]
    public void CheckStatus_Admin_NoJob_Idle() {
        var sp = new ServiceCollection().BuildServiceProvider();
        var c = Make(UserRole.Admin, sp, new DeviceJobTracker(TimeProvider.System));
        var r = c.CheckStatus("frame-android");
        var ok = Assert.IsType<OkObjectResult>(r);
        Assert.Contains("idle", ok.Value!.ToString());
    }

    [Fact]
    public void CheckStatus_Admin_RunningJob_ReportsRunning() {
        var jobs = new DeviceJobTracker(TimeProvider.System);
        jobs.TryStart("frame-android", "checking store...");
        var sp = new ServiceCollection().BuildServiceProvider();
        var c = Make(UserRole.Admin, sp, jobs);
        var r = c.CheckStatus("frame-android");
        var ok = Assert.IsType<OkObjectResult>(r);
        Assert.Contains("running", ok.Value!.ToString());
    }

    private sealed class FakeUser(UserRole role) : ICurrentUser {
        public bool IsAuthenticated => true;
        public Guid? UserId => null;
        public string? DiscordId => "123";
        public string? Username => "tester";
        public string? Avatar => null;
        public string? AvatarUrl => null;
        public UserRole Role => role;
        public bool IsSupporter => false;
        public bool IsAtLeast(UserRole need) => UserRoles.IsAtLeast(Role, need);
    }
}
