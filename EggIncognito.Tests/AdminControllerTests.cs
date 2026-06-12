using EggIncognito.Controllers;
using EggIncognito.Data.Models;
using EggIncognito.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace EggIncognito.Tests;

public class AdminControllerTests
{
    private sealed class FakeUser(UserRole role, string id = "me") : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? DiscordId => id;
        public string? Username => "u";
        public string? Avatar => null;
        public UserRole Role => role;
        public bool IsAtLeast(UserRole need) => UserRoles.IsAtLeast(role, need);
    }
    private sealed class EmptyServices : IServiceProvider { public object? GetService(Type t) => null; }

    private static AdminController Controller(UserRole role, string id = "me")
        => new(new FakeUser(role, id), new EmptyServices());

    [Fact]
    public async Task NonAdmin_Users_Is403()
    {
        var r = await Controller(UserRole.Contributor).Users();
        Assert.Equal(403, ((IStatusCodeActionResult)r).StatusCode);
    }

    [Fact]
    public async Task Admin_PassesGate_Then503NoDb()
    {
        var r = await Controller(UserRole.Admin).Users();
        Assert.Equal(503, ((IStatusCodeActionResult)r).StatusCode);
    }

    [Fact]
    public async Task Admin_SelfDemote_Is400()
    {
        var r = await Controller(UserRole.Admin, "me").SetUserRole("me", new AdminController.SetRole("viewer"));
        Assert.Equal(400, ((IStatusCodeActionResult)r).StatusCode);
    }

    // Unknown roles must 400, never coerce to viewer: a typo'd role would otherwise silently demote
    // (200) and a malformed role aimed at yourself would slip past the self-lockout guard.
    [Theory]
    [InlineData("superuser")]
    [InlineData("")]
    [InlineData(null)]
    public async Task Admin_SetUnknownRole_Is400(string? role)
    {
        var r = await Controller(UserRole.Admin).SetUserRole("other", new AdminController.SetRole(role!));
        var bad = Assert.IsType<BadRequestObjectResult>(r);
        Assert.Contains("unknown role", bad.Value!.ToString());
    }

    [Fact]
    public async Task Admin_SelfWithMalformedRole_Is400_NotDemoted()
    {
        var r = await Controller(UserRole.Admin, "me").SetUserRole("me", new AdminController.SetRole("admln"));
        Assert.Equal(400, ((IStatusCodeActionResult)r).StatusCode);
    }
}
