using EggIncognito.Data.Models;

namespace EggIncognito.Tests;

public class UserRolesTests
{
    [Theory]
    [InlineData("admin", UserRole.Admin)]
    [InlineData("contributor", UserRole.Contributor)]
    [InlineData("viewer", UserRole.Viewer)]
    [InlineData("ADMIN", UserRole.Admin)]
    [InlineData(null, UserRole.Viewer)]
    [InlineData("nonsense", UserRole.Viewer)]
    public void Parse_MapsOrDefaultsToViewer(string? input, UserRole expected)
        => Assert.Equal(expected, UserRoles.Parse(input));

    [Fact]
    public void ToName_IsLowercase() => Assert.Equal("contributor", UserRoles.ToName(UserRole.Contributor));

    [Theory]
    [InlineData(UserRole.Admin, UserRole.Contributor, true)]
    [InlineData(UserRole.Contributor, UserRole.Contributor, true)]
    [InlineData(UserRole.Viewer, UserRole.Contributor, false)]
    public void IsAtLeast_ComparesByRank(UserRole have, UserRole need, bool expected)
        => Assert.Equal(expected, UserRoles.IsAtLeast(have, need));
}
