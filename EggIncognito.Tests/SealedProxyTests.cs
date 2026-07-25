using System.Net;
using EggIdentity.Contract;
using EggIncognito.Services;

namespace EggIncognito.Tests;

public class SealedProxyTests {
    private static SealedProxy NewProxy(SealedProxyOptions options, ISupporterStatus supporters,
        IHttpClientFactory? factory = null)
        => new(options, factory ?? new StubHttpFactory(), supporters);

    private static SealedProxyOptions Configured(string? user = null, string? pass = null) => new() {
        UpstreamUrl = "http://proxy.internal:8888",
        Username = user,
        Password = pass
    };

    [Fact]
    public void IsConfigured_EmptyUpstream_False()
        => Assert.False(NewProxy(new SealedProxyOptions(), new FakeSupporters(true)).IsConfigured);

    [Fact]
    public void IsConfigured_WithUpstream_True()
        => Assert.True(NewProxy(Configured(), new FakeSupporters(true)).IsConfigured);

    [Fact]
    public async Task CanUse_Unconfigured_False() {
        var supporters = new FakeSupporters(true);
        var proxy = NewProxy(new SealedProxyOptions(), supporters);
        Assert.False(await proxy.CanUseAsync(new FakeUser(true, true)));
        Assert.Equal(0, supporters.Calls);
    }

    [Fact]
    public async Task CanUse_Anonymous_False() {
        var supporters = new FakeSupporters(true);
        var proxy = NewProxy(Configured(), supporters);
        Assert.False(await proxy.CanUseAsync(new FakeUser(false, false)));
        Assert.Equal(0, supporters.Calls);
    }

    [Fact]
    public async Task CanUse_NonSupporter_False() {
        var supporters = new FakeSupporters(true);
        var proxy = NewProxy(Configured(), supporters);
        Assert.False(await proxy.CanUseAsync(new FakeUser(true, false)));
        Assert.Equal(0, supporters.Calls);
    }

    [Fact]
    public async Task CanUse_SupporterClaimButLiveCheckFails_False() {
        var supporters = new FakeSupporters(false);
        var proxy = NewProxy(Configured(), supporters);
        Assert.False(await proxy.CanUseAsync(new FakeUser(true, true)));
        Assert.Equal(1, supporters.Calls);
    }

    [Fact]
    public async Task CanUse_SupporterAndLiveCheckPasses_True() {
        var supporters = new FakeSupporters(true);
        var proxy = NewProxy(Configured(), supporters);
        Assert.True(await proxy.CanUseAsync(new FakeUser(true, true)));
        Assert.Equal(1, supporters.Calls);
    }

    [Fact]
    public void CreateEgressClient_UsesNamedEgressClient() {
        var factory = new StubHttpFactory();
        var proxy = NewProxy(Configured(), new FakeSupporters(true), factory);
        _ = proxy.CreateEgressClient();
        Assert.Equal(SealedProxy.EgressClientName, factory.LastName);
    }

    [Fact]
    public void BuildProxy_EmptyUrl_Null()
        => Assert.Null(SealedProxy.BuildProxy(new SealedProxyOptions()));

    [Fact]
    public void BuildProxy_InvalidUrl_Null()
        => Assert.Null(SealedProxy.BuildProxy(new SealedProxyOptions { UpstreamUrl = "not a url" }));

    [Fact]
    public void BuildProxy_ValidUrl_NoCreds_ProxyWithoutCredentials() {
        var proxy = Assert.IsType<WebProxy>(SealedProxy.BuildProxy(Configured()));
        Assert.Null(proxy.Credentials);
        Assert.Equal("http://proxy.internal:8888/", proxy.Address?.ToString());
    }

    [Fact]
    public void BuildProxy_ValidUrl_WithCreds_ProxyCarriesCredentials() {
        var proxy = Assert.IsType<WebProxy>(SealedProxy.BuildProxy(Configured("user", "pass")));
        var cred = Assert.IsType<NetworkCredential>(proxy.Credentials);
        Assert.Equal("user", cred.UserName);
        Assert.Equal("pass", cred.Password);
    }

    private sealed class FakeUser(bool authed, bool supporter, string? id = "tester") : ICurrentUser {
        public bool IsAuthenticated => authed;
        public Guid? UserId => null;
        public string? DiscordId => authed ? id : null;
        public string? Username => authed ? "tester" : null;
        public string? Avatar => null;
        public string? AvatarUrl => null;
        public UserRole Role => UserRole.Viewer;
        public bool IsSupporter => supporter;
        public bool IsAtLeast(UserRole need) => UserRoles.IsAtLeast(UserRole.Viewer, need);
    }

    private sealed class FakeSupporters(bool result) : ISupporterStatus {
        public int Calls { get; private set; }

        public Task<bool> CheckAsync(string discordId, CancellationToken ct = default) {
            Calls++;
            return Task.FromResult(result);
        }
    }

    private sealed class StubHttpFactory : IHttpClientFactory {
        public string? LastName { get; private set; }

        public HttpClient CreateClient(string name) {
            LastName = name;
            return new HttpClient();
        }
    }
}
