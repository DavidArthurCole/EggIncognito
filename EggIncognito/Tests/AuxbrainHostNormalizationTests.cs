using EggIncognito.Core.Services;

namespace EggIncognito.Tests;

public class AuxbrainHostNormalizationTests {
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
    [InlineData("2001:db8::1")]
    [InlineData("[unclosed")]
    [InlineData("[]")]
    public void NormalizeHost_RejectsNonHosts(string authority) =>
        Assert.Equal("", AuxbrainHosts.NormalizeHost(authority));

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

    [Theory]
    [InlineData("-svc-dot-auxbrainhome.appspot.com")]
    [InlineData("svc--dot-auxbrainhome.appspot.com")]
    public void IsAuxbrain_RejectsEdgeHyphenServiceLabels(string host) =>
        Assert.False(AuxbrainHosts.IsAuxbrain(host));

    [Theory]
    [InlineData("svc-dot-auxbrainhome.appspot.com")]
    [InlineData("my-service-dot-auxbrainhome.appspot.com")]
    public void IsAuxbrain_AcceptsLegalServiceLabels(string host) =>
        Assert.True(AuxbrainHosts.IsAuxbrain(host));
}
