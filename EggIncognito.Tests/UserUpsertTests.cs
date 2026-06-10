using System.Security.Claims;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;

namespace EggIncognito.Tests;

public class UserUpsertTests
{
    private static ClaimsPrincipal Principal(string id, string name, string? avatar)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, id), new(ClaimTypes.Name, name) };
        if (avatar is not null) claims.Add(new Claim("urn:discord:avatar:hash", avatar));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Discord"));
    }

    [Fact]
    public void Extract_PullsIdNameAvatar()
    {
        var info = UserUpsert.Extract(Principal("123", "alice", "abc"));
        Assert.Equal("123", info.DiscordId);
        Assert.Equal("alice", info.Username);
        Assert.Equal("abc", info.Avatar);
    }

    [Fact]
    public void Extract_NullAvatarWhenAbsent()
    {
        var info = UserUpsert.Extract(Principal("123", "alice", null));
        Assert.Null(info.Avatar);
    }

    [Fact]
    public void ApplyToNew_SetsAllAndTimestamps()
    {
        var row = new User();
        UserUpsert.Apply(row, new UserUpsert.Info("123", "alice", "abc"), isNew: true, now: DateTimeOffset.UnixEpoch);
        Assert.Equal("123", row.DiscordId);
        Assert.Equal("alice", row.Username);
        Assert.Equal(DateTimeOffset.UnixEpoch, row.LastLoginAt);
    }

    [Fact]
    public void ApplyToExisting_BumpsLoginAndRefreshesProfile_NotCreatedAt()
    {
        var created = DateTimeOffset.UnixEpoch;
        var row = new User { DiscordId = "123", Username = "old", CreatedAt = created, LastLoginAt = created };
        var later = created.AddDays(1);
        UserUpsert.Apply(row, new UserUpsert.Info("123", "alice", "abc"), isNew: false, now: later);
        Assert.Equal("alice", row.Username);
        Assert.Equal(later, row.LastLoginAt);
        Assert.Equal(created, row.CreatedAt); // unchanged
    }
}
