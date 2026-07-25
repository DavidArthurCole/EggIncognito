using System.Security.Claims;
using EggIncognito.Services;

namespace EggIncognito.Tests;

public class SupporterStatusTests {
    private const string RoleId = "1514877193162068018";

    [Fact]
    public void ParseHasRole_RolePresent_True() {
        const string json = $$"""{"user":{"id":"1"},"roles":["111","{{RoleId}}","222"]}""";
        Assert.True(SupporterStatus.ParseHasRole(json, RoleId));
    }

    [Fact]
    public void ParseHasRole_RoleAbsent_False() {
        const string json = """{"roles":["111","222"]}""";
        Assert.False(SupporterStatus.ParseHasRole(json, RoleId));
    }

    [Fact]
    public void ParseHasRole_NoRolesProperty_False()
        => Assert.False(SupporterStatus.ParseHasRole("""{"user":{"id":"1"}}""", RoleId));

    [Fact]
    public void ParseHasRole_MalformedJson_False()
        => Assert.False(SupporterStatus.ParseHasRole("not json", RoleId));

    [Fact]
    public void Stamp_AddsClaim() {
        var identity = new ClaimsIdentity("test");
        SupporterClaims.Stamp(identity, true);
        Assert.Equal("true", identity.FindFirst(SupporterClaims.ClaimType)?.Value);
    }

    [Fact]
    public void Stamp_ReplacesExistingClaim() {
        var identity = new ClaimsIdentity("test");
        SupporterClaims.Stamp(identity, true);
        SupporterClaims.Stamp(identity, false);
        Assert.Equal("false", identity.FindFirst(SupporterClaims.ClaimType)?.Value);
        Assert.Single(identity.FindAll(SupporterClaims.ClaimType));
    }
}
