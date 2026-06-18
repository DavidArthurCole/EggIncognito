using EggIncognito.Controllers;
using EggIncognito.Data.Models;
using EggIncognito.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace EggIncognito.Tests;

// BackfillController gating, DB-free. The admin check runs before the importer resolve, so a non-admin
// 403s and an admin with no DB (no importer registered) 503s, regardless of state.
public class BackfillApiTests
{
    private sealed class FakeUser(UserRole role) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? DiscordId => "me";
        public string? Username => "u";
        public string? Avatar => null;
        public UserRole Role => role;
        public bool IsSupporter => false;
        public bool IsAtLeast(UserRole need) => UserRoles.IsAtLeast(role, need);
    }

    // No importers registered (the no-DB path).
    private sealed class EmptyServices : IServiceProvider { public object? GetService(Type t) => null; }

    private static BackfillController Controller(UserRole role)
        => new(new EmptyServices(), new FakeUser(role));

    [Fact]
    public void Elgranjero_NonAdmin_Is403() =>
        Assert.Equal(403, ((IStatusCodeActionResult)Controller(UserRole.Contributor).Elgranjero()).StatusCode);

    [Fact]
    public void Elgranjero_Admin_NoDb_Is503() =>
        Assert.Equal(503, ((IStatusCodeActionResult)Controller(UserRole.Admin).Elgranjero()).StatusCode);

    [Fact]
    public void PlayStore_NonAdmin_Is403() =>
        Assert.Equal(403, ((IStatusCodeActionResult)Controller(UserRole.Viewer).PlayStore()).StatusCode);

    [Fact]
    public void AppStore_Admin_NoDb_Is503() =>
        Assert.Equal(503, ((IStatusCodeActionResult)Controller(UserRole.Admin).AppStore()).StatusCode);

    [Fact]
    public void List_NonAdmin_Is403() =>
        Assert.Equal(403, ((IStatusCodeActionResult)Controller(UserRole.Viewer).List("fandom")).StatusCode);

    [Fact]
    public void List_Admin_UnknownSource_Is400() =>
        Assert.Equal(400, ((IStatusCodeActionResult)Controller(UserRole.Admin).List("bogus")).StatusCode);

    [Fact]
    public void List_Admin_KnownSource_NoDb_Is503() =>
        Assert.Equal(503, ((IStatusCodeActionResult)Controller(UserRole.Admin).List("fandom")).StatusCode);

    [Fact]
    public async Task ApkExtract_NonAdmin_Is403() =>
        Assert.Equal(403, ((IStatusCodeActionResult)await Controller(UserRole.Contributor)
            .ApkExtract(new BackfillController.ApkExtractRequest("1.0.0"), CancellationToken.None)).StatusCode);

    // No local extract + no runner agent configured -> 501 not-configured.
    [Fact]
    public async Task ApkExtract_Admin_NotConfigured_Is501() =>
        Assert.Equal(501, ((IStatusCodeActionResult)await Controller(UserRole.Admin)
            .ApkExtract(new BackfillController.ApkExtractRequest("1.0.0"), CancellationToken.None)).StatusCode);

    [Fact]
    public async Task ApkExtract_Admin_BlankVersion_Is400() =>
        Assert.Equal(400, ((IStatusCodeActionResult)await Controller(UserRole.Admin)
            .ApkExtract(new BackfillController.ApkExtractRequest(""), CancellationToken.None)).StatusCode);

    [Fact]
    public async Task Status_NonAdmin_Is403() =>
        Assert.Equal(403, ((IStatusCodeActionResult)await Controller(UserRole.Viewer)
            .Status(CancellationToken.None)).StatusCode);

    // Admin with no DB: status degrades to an empty 200 (no job store), never 503.
    [Fact]
    public async Task Status_Admin_NoDb_Is200_Empty()
    {
        var result = await Controller(UserRole.Admin).Status(CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(200, ok.StatusCode);
    }

    // Known-versions discovery list is PUBLIC: anon/viewer gets 200 (empty without a DB), never 403.
    [Fact]
    public async Task Known_Anon_Is200_Empty()
    {
        var result = await Controller(UserRole.Viewer).Known(CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(200, ok.StatusCode);
    }

    [Fact]
    public async Task RunnerResync_NonAdmin_Is403() =>
        Assert.Equal(403, ((IStatusCodeActionResult)
            await Controller(UserRole.Viewer).RunnerResync(null, default)).StatusCode);

    [Fact]
    public async Task RunnerResync_Admin_Unconfigured_Is501() =>
        Assert.Equal(501, ((IStatusCodeActionResult)
            await Controller(UserRole.Admin).RunnerResync(null, default)).StatusCode);
}
