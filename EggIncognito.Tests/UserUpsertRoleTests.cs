using EggIncognito.Data.Services;

namespace EggIncognito.Tests;

public class UserUpsertRoleTests
{
    private static AdminAllowlist Allow(params string[] ids) => new(ids.ToHashSet());

    [Fact]
    public void NewUser_NotAllowlisted_IsViewer()
        => Assert.Equal("viewer", UserUpsert.ResolveRole(existingRole: null, "123", Allow()));

    [Fact]
    public void NewUser_Allowlisted_IsAdmin()
        => Assert.Equal("admin", UserUpsert.ResolveRole(existingRole: null, "123", Allow("123")));

    [Fact]
    public void Returning_Allowlisted_BelowAdmin_IsPromoted()
        => Assert.Equal("admin", UserUpsert.ResolveRole(existingRole: "viewer", "123", Allow("123")));

    [Fact]
    public void Returning_Normal_KeepsStoredRole()
        => Assert.Equal("contributor", UserUpsert.ResolveRole(existingRole: "contributor", "123", Allow()));

    [Fact]
    public void Returning_Allowlisted_AlreadyAdmin_StaysAdmin()
        => Assert.Equal("admin", UserUpsert.ResolveRole(existingRole: "admin", "123", Allow("123")));
}
