using EggIncognito.Controllers;
using EggIncognito.Data.Models;
using EggIncognito.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace EggIncognito.Tests;

// The /api/inspector/send sealed-proxy gate: a request that asks for sealed mode without the perk is
// rejected 403 before any egress; a non-sealed request never consults the perk. Egress itself is not
// exercised (it would hit the network); these assert the gate, the security-critical seam.
public class InspectorSealedSendTests
{
    private sealed class FakeAppMode(AppMode mode) : IAppMode
    {
        public AppMode Mode => mode;
        public bool CanCapture => false;
        public bool CanWrite => false;
        public bool HostedCaptureEnabled => false;
    }

    private sealed class FakeUser(bool authed, bool supporter) : ICurrentUser
    {
        public bool IsAuthenticated => authed;
        public string? DiscordId => authed ? "tester" : null;
        public string? Username => authed ? "tester" : null;
        public string? Avatar => null;
        public UserRole Role => UserRole.Viewer;
        public bool IsSupporter => supporter;
        public bool IsAtLeast(UserRole need) => UserRoles.IsAtLeast(UserRole.Viewer, need);
    }

    private sealed class FakeSealedProxy(bool configured, bool canUse) : ISealedProxy
    {
        public int CanUseCalls { get; private set; }
        public bool IsConfigured => configured;
        public Task<bool> CanUseAsync(ICurrentUser user, CancellationToken ct = default)
        {
            CanUseCalls++;
            return Task.FromResult(canUse);
        }
        public HttpClient CreateEgressClient() => new();
    }

    private static InspectorApiController NewController(
        IAppMode appMode, ICurrentUser user, ISealedProxy sealedProxy)
    {
        var c = new InspectorApiController(
            catalog: null!, reflection: null!, pipeline: null!, httpFactory: null!,
            appMode, user, sealedProxy, NullLogger<InspectorApiController>.Instance);
        c.ControllerContext = new() { HttpContext = new DefaultHttpContext() };
        return c;
    }

    private static InspectorApiController.SendRequest SealedSend() =>
        new("https://www.auxbrain.com/ei/first_contact", "data=x", "EggIncFirstContactResponse", Sealed: true);

    [Fact]
    public async Task Send_SealedRequest_NotConfigured_403()
    {
        var sealedProxy = new FakeSealedProxy(configured: false, canUse: false);
        var controller = NewController(
            new FakeAppMode(AppMode.Local), new FakeUser(authed: true, supporter: true), sealedProxy);

        var ex = await Assert.ThrowsAsync<ApiException>(() => controller.Send(SealedSend()));
        Assert.Equal(StatusCodes.Status403Forbidden, ex.Status);
        Assert.Equal(1, sealedProxy.CanUseCalls);
    }

    [Fact]
    public async Task Send_SealedRequest_NonSupporter_403()
    {
        var sealedProxy = new FakeSealedProxy(configured: true, canUse: false);
        var controller = NewController(
            new FakeAppMode(AppMode.Local), new FakeUser(authed: true, supporter: false), sealedProxy);

        var ex = await Assert.ThrowsAsync<ApiException>(() => controller.Send(SealedSend()));
        Assert.Equal(StatusCodes.Status403Forbidden, ex.Status);
    }

    [Fact]
    public async Task Send_HostedAnonymous_403_BeforeSealedCheck()
    {
        var sealedProxy = new FakeSealedProxy(configured: true, canUse: true);
        var controller = NewController(
            new FakeAppMode(AppMode.Hosted), new FakeUser(authed: false, supporter: false), sealedProxy);

        var ex = await Assert.ThrowsAsync<ApiException>(() => controller.Send(SealedSend()));
        Assert.Equal(StatusCodes.Status403Forbidden, ex.Status);
        Assert.Equal(0, sealedProxy.CanUseCalls); // login gate fires first
    }
}
