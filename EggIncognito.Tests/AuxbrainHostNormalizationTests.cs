using EggIncognito.Services;

namespace EggIncognito.Tests;

// AuxbrainHosts is the single place host:port handling lives. Callers pass uri.Host or a raw
// CONNECT authority ("www.auxbrain.com:443"); before NormalizeHost existed, the port made auxbrain
// traffic tunnel undecrypted. These guard the normalization and the GAE service-label rules.
public class AuxbrainHostNormalizationTests
{
    [Theory]
    [InlineData("www.auxbrain.com", "www.auxbrain.com")]
    [InlineData("www.auxbrain.com:443", "www.auxbrain.com")]
    [InlineData("auxbrain.com:8080", "auxbrain.com")]
    [InlineData("[::1]", "::1")]
    [InlineData("[2001:db8::1]:443", "2001:db8::1")]
    public void NormalizeHost_StripsPortAndBrackets(string authority, string expected) =>
        Assert.Equal(expected, AuxbrainHosts.NormalizeHost(authority));

    [Theory]
    [InlineData("")]
    [InlineData("www.auxbrain.com/path")]
    [InlineData("https://www.auxbrain.com")]
    [InlineData("2001:db8::1")] // raw IPv6, no brackets
    [InlineData("[unclosed")]
    [InlineData("[]")]
    public void NormalizeHost_RejectsNonHosts(string authority) =>
        Assert.Equal("", AuxbrainHosts.NormalizeHost(authority));

    // The CONNECT-authority regression: auxbrain with a port must still be auxbrain.
    [Theory]
    [InlineData("www.auxbrain.com:443")]
    [InlineData("auxbrain.com:443")]
    [InlineData("auxbrainhome.appspot.com:443")]
    [InlineData("ctx-dot-auxbrainhome.appspot.com:443")]
    public void IsAuxbrain_AcceptsAuthorityWithPort(string authority) =>
        Assert.True(AuxbrainHosts.IsAuxbrain(authority));

    [Theory]
    [InlineData("evil.com:443")]
    [InlineData("auxbrainhome.appspot.com.evil.com:443")]
    [InlineData("www.auxbrain.com/evil")]
    [InlineData("[2001:db8::1]:443")]
    [InlineData("")]
    public void IsAuxbrain_RejectsBadAuthorities(string authority) =>
        Assert.False(AuxbrainHosts.IsAuxbrain(authority));

    // GAE service labels must be a real DNS label: no leading or trailing hyphen.
    [Theory]
    [InlineData("-svc-dot-auxbrainhome.appspot.com")]
    [InlineData("svc--dot-auxbrainhome.appspot.com")] // service "svc-" ends with a hyphen
    public void IsAuxbrain_RejectsEdgeHyphenServiceLabels(string host) =>
        Assert.False(AuxbrainHosts.IsAuxbrain(host));

    [Theory]
    [InlineData("svc-dot-auxbrainhome.appspot.com")]
    [InlineData("my-service-dot-auxbrainhome.appspot.com")] // interior hyphen stays legal
    public void IsAuxbrain_AcceptsLegalServiceLabels(string host) =>
        Assert.True(AuxbrainHosts.IsAuxbrain(host));
}
