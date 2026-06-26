using EggIncognito.Controllers;
using EggIncognito.Services;

namespace EggIncognito.Tests;

// Guards the /send open-proxy allowlist, including the Google App Engine "-dot-"
// service-host form that the original "." suffix check wrongly rejected.
public class HostAllowlistTests
{
    [Theory]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("www.auxbrain.com")]
    [InlineData("auxbrain.com")]
    [InlineData("auxbrainhome.appspot.com")]
    [InlineData("ctx-dot-auxbrainhome.appspot.com")]
    [InlineData("some-service-dot-auxbrainhome.appspot.com")]
    public void Allows_LegitHosts(string host) =>
        Assert.True(InspectorApiController.IsAllowedHost(host));

    [Theory]
    [InlineData("evil.com")]
    [InlineData("auxbrainhome.appspot.com.evil.com")]
    [InlineData("evil.com-dot-auxbrainhome.appspot.com")] // dot in the service label
    [InlineData("notauxbrain.com")]
    [InlineData("-dot-auxbrainhome.appspot.com")] // empty service label
    public void Rejects_BadHosts(string host) =>
        Assert.False(InspectorApiController.IsAllowedHost(host));

    // The capture proxy decrypts via AuxbrainHosts.IsAuxbrain; the Inspector allows the same set
    // PLUS localhost. Guard that the shared rule and the allowlist never drift apart: for every
    // non-localhost host, IsAllowedHost == IsAuxbrain. (localhost is allowed but not auxbrain.)
    [Theory]
    [InlineData("www.auxbrain.com")]
    [InlineData("auxbrain.com")]
    [InlineData("auxbrainhome.appspot.com")]
    [InlineData("ctx-dot-auxbrainhome.appspot.com")]
    [InlineData("evil.com")]
    [InlineData("evil.com-dot-auxbrainhome.appspot.com")]
    [InlineData("notauxbrain.com")]
    public void AllowlistMatchesAuxbrainRule_ForNonLocalhost(string host) =>
        Assert.Equal(AuxbrainHosts.IsAuxbrain(host), InspectorApiController.IsAllowedHost(host));

    [Theory]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    public void Localhost_IsAllowed_ButNotAuxbrain(string host)
    {
        Assert.True(InspectorApiController.IsAllowedHost(host));
        Assert.False(AuxbrainHosts.IsAuxbrain(host));
    }

    // "Mock (this instance)" must work on a public deploy: the instance's own request host is allowed
    // when passed as selfHost, case-insensitively, but never when it is not this instance.
    [Fact]
    public void SelfHost_IsAllowed_OnlyWhenItMatches()
    {
        const string self = "eggincognito.davidarthurcole.me";
        Assert.True(InspectorApiController.IsAllowedHost(self, self));
        Assert.True(InspectorApiController.IsAllowedHost("EggIncognito.DavidArthurCole.ME", self));
        Assert.False(InspectorApiController.IsAllowedHost(self)); // no selfHost = rejected
        Assert.False(InspectorApiController.IsAllowedHost("evil.com", self)); // different host still rejected
    }
}
