using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EggIncognito.Tests;

// DB-gated: these need a real Postgres connection to exercise transactional insert/conflict
// behavior in-process. No SkippableFact/Skip.If convention exists in this repo (confirmed via
// grep), so this follows the established DB-test pattern of a plain Fact guarded by an early
// return when EGI_TEST_PG_CONN is unset, per the tests-DB-free repo rule (see
// DeviceStatusStoreTests for the hard-skip variant; this one opts in when the env var is set).
public class AuthentikIdentityResolverTests
{
    private static string? ConnString => Environment.GetEnvironmentVariable("EGI_TEST_PG_CONN");

    private static EggIncognitoDbContext MakeDb()
    {
        var options = new DbContextOptionsBuilder<EggIncognitoDbContext>()
            .UseNpgsql(ConnString)
            .Options;
        return new EggIncognitoDbContext(options);
    }

    [Fact]
    public async Task ResolveAsync_NewSub_NoDiscordId_CreatesNewUser()
    {
        if (string.IsNullOrEmpty(ConnString)) return; // no live Postgres in this run
        await using var db = MakeDb();
        await db.Database.EnsureCreatedAsync();
        var resolver = new AuthentikIdentityResolver(db);

        var userId = await resolver.ResolveAsync("sub-new-1", null, CancellationToken.None);

        var identity = await db.Identities.SingleAsync(i => i.Provider == "authentik" && i.Subject == "sub-new-1");
        Assert.Equal(userId, identity.UserId);
    }

    [Fact]
    public async Task ResolveAsync_ExistingSub_ReturnsSameUserId()
    {
        if (string.IsNullOrEmpty(ConnString)) return; // no live Postgres in this run
        await using var db = MakeDb();
        await db.Database.EnsureCreatedAsync();
        var resolver = new AuthentikIdentityResolver(db);

        var first = await resolver.ResolveAsync("sub-existing-1", null, CancellationToken.None);
        var second = await resolver.ResolveAsync("sub-existing-1", null, CancellationToken.None);

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task ResolveAsync_MatchingDiscordId_AutoLinksExistingUser()
    {
        if (string.IsNullOrEmpty(ConnString)) return; // no live Postgres in this run
        await using var db = MakeDb();
        await db.Database.EnsureCreatedAsync();
        var existingUserId = Guid.NewGuid();
        db.Users.Add(new User { UserId = existingUserId, DiscordId = "discord-42", Username = "alice", Role = "viewer" });
        db.Identities.Add(new Identity { UserId = existingUserId, Provider = "discord", Subject = "discord-42" });
        await db.SaveChangesAsync();
        var resolver = new AuthentikIdentityResolver(db);

        var linkedUserId = await resolver.ResolveAsync("sub-link-1", "discord-42", CancellationToken.None);

        Assert.Equal(existingUserId, linkedUserId);
        Assert.True(await db.Identities.AnyAsync(i => i.Provider == "authentik" && i.Subject == "sub-link-1" && i.UserId == existingUserId));
    }
}
