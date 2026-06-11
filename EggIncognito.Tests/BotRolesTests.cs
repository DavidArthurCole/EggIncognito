using EggIncognito.Bot;

namespace EggIncognito.Tests;

public class BotRolesTests
{
    [Fact]
    public void NeedsRole_TrueWhenAbsent()
    {
        Assert.True(BotRoles.NeedsRole(new ulong[] { 111, 222 }, 999));
    }

    [Fact]
    public void NeedsRole_FalseWhenPresent()
    {
        Assert.False(BotRoles.NeedsRole(new ulong[] { 111, 999, 222 }, 999));
    }

    [Fact]
    public void NeedsRole_TrueWhenEmpty()
    {
        Assert.True(BotRoles.NeedsRole(Array.Empty<ulong>(), 999));
    }
}
