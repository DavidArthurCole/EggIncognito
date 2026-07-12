using System.Net;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.Tools;
using Microsoft.EntityFrameworkCore;
using SyncKit.Identity.Client;
using Xunit;

namespace EggIncognito.Tests;

public class CaptureUserIdBackfillTests
{
    private static IdentityApiClient StubIdentity(Guid resolvedUserId)
    {
        var http = new HttpClient(new StubHttpMessageHandler(req =>
            StubHttpMessageHandler.Json(HttpStatusCode.OK,
                $$"""{"userId":"{{resolvedUserId}}","role":"viewer","discordId":null,"isNew":false}""")))
        { BaseAddress = new Uri("http://identity.local") };
        return new IdentityApiClient(http);
    }

    [Fact(Skip = "requires Postgres; no EF test provider per tests-DB-free repo rule")]
    public async Task RunAsync_BackfillsEmptyUserId_LeavesPopulatedRowsUntouched()
    {
        var opts = new DbContextOptionsBuilder<EggIncognitoDbContext>()
            .UseNpgsql("Host=frame;Port=5432;Database=eggincognito_test;Username=ei;Password=ei").Options;
        await using var db = new EggIncognitoDbContext(opts);

        var resolvedId = Guid.NewGuid();
        var alreadySetId = Guid.NewGuid();

        db.CaptureProxyAddrs.Add(new CaptureProxyAddr { UserId = Guid.Empty, DiscordId = "111", Addr = "2a01::1", CreatedAt = DateTimeOffset.UtcNow });
        db.CaptureProxyAddrs.Add(new CaptureProxyAddr { UserId = alreadySetId, DiscordId = "222", Addr = "2a01::2", CreatedAt = DateTimeOffset.UtcNow });
        db.CaptureUserCas.Add(new CaptureUserCa { UserId = Guid.Empty, DiscordId = "111", Pfx = [1, 2, 3], Thumbprint = "abc" });
        db.CaptureUserCas.Add(new CaptureUserCa { UserId = alreadySetId, DiscordId = "222", Pfx = [4, 5, 6], Thumbprint = "def" });
        await db.SaveChangesAsync();

        var updated = await CaptureUserIdBackfill.RunAsync(db, StubIdentity(resolvedId), CancellationToken.None);

        Assert.Equal(2, updated);
        var addr = await db.CaptureProxyAddrs.AsNoTracking().FirstAsync(a => a.DiscordId == "111");
        Assert.Equal(resolvedId, addr.UserId);
        var untouchedAddr = await db.CaptureProxyAddrs.AsNoTracking().FirstAsync(a => a.DiscordId == "222");
        Assert.Equal(alreadySetId, untouchedAddr.UserId);

        var ca = await db.CaptureUserCas.AsNoTracking().FirstAsync(c => c.DiscordId == "111");
        Assert.Equal(resolvedId, ca.UserId);
        var untouchedCa = await db.CaptureUserCas.AsNoTracking().FirstAsync(c => c.DiscordId == "222");
        Assert.Equal(alreadySetId, untouchedCa.UserId);
    }
}

public class OwnerAuthorUserIdBackfillTests
{
    // Counts ResolveAsync calls (one HTTP POST each) so the cache-hit assertion below is a real
    // count, not an inference from row totals.
    private static (IdentityApiClient Client, Func<int> CallCount) CountingIdentity(Guid resolvedUserId)
    {
        var calls = 0;
        var http = new HttpClient(new StubHttpMessageHandler(req =>
        {
            calls++;
            return StubHttpMessageHandler.Json(HttpStatusCode.OK,
                $$"""{"userId":"{{resolvedUserId}}","role":"viewer","discordId":null,"isNew":false}""");
        }))
        { BaseAddress = new Uri("http://identity.local") };
        return (new IdentityApiClient(http), () => calls);
    }

    [Fact(Skip = "requires Postgres; no EF test provider per tests-DB-free repo rule")]
    public async Task RunAsync_SameDiscordIdAcrossTables_ResolvesOnceViaCache()
    {
        var opts = new DbContextOptionsBuilder<EggIncognitoDbContext>()
            .UseNpgsql("Host=frame;Port=5432;Database=eggincognito_test;Username=ei;Password=ei").Options;
        await using var db = new EggIncognitoDbContext(opts);

        var resolvedId = Guid.NewGuid();
        var (identity, callCount) = CountingIdentity(resolvedId);

        // Same Discord ID string ("111") owns a doc and a stored endpoint; a null owner_user_id
        // row and an already-migrated-shaped GUID-string row should both be left alone.
        db.Docs.Add(new Doc { SubjectKind = "message", SubjectKey = "A", BodyMd = "x" });
        db.StoredEndpoints.Add(new StoredEndpoint { Path = "/a", ResponseJson = "{}", ResponseType = "T" });
        await db.SaveChangesAsync();

        // Model columns are already Guid?, so seed the raw text values directly to simulate pre-migration state.
        var conn = (Npgsql.NpgsqlConnection)db.Database.GetDbConnection();
        await conn.OpenAsync();
        await using (var cmd = new Npgsql.NpgsqlCommand("UPDATE docs SET owner_user_id = '111' WHERE subject_key = 'A'", conn))
            await cmd.ExecuteNonQueryAsync();
        await using (var cmd = new Npgsql.NpgsqlCommand("UPDATE stored_endpoints SET owner_user_id = '111' WHERE path = '/a'", conn))
            await cmd.ExecuteNonQueryAsync();

        var updated = await OwnerAuthorUserIdBackfill.RunAsync(db, identity, CancellationToken.None);

        Assert.Equal(2, updated);
        Assert.Equal(1, callCount());

        await using var check = new Npgsql.NpgsqlCommand(
            "SELECT owner_user_id FROM docs WHERE subject_key = 'A'", conn);
        var docOwner = (Guid)(await check.ExecuteScalarAsync())!;
        Assert.Equal(resolvedId, docOwner);
    }

    [Fact(Skip = "requires Postgres; no EF test provider per tests-DB-free repo rule")]
    public async Task RunAsync_NullOrAlreadyGuidRows_LeftUntouched()
    {
        var opts = new DbContextOptionsBuilder<EggIncognitoDbContext>()
            .UseNpgsql("Host=frame;Port=5432;Database=eggincognito_test;Username=ei;Password=ei").Options;
        await using var db = new EggIncognitoDbContext(opts);

        var resolvedId = Guid.NewGuid();
        var (identity, callCount) = CountingIdentity(resolvedId);

        var untouchedGuid = Guid.NewGuid();
        db.Docs.Add(new Doc { SubjectKind = "message", SubjectKey = "null-owner", BodyMd = "x", OwnerUserId = null });
        db.Docs.Add(new Doc { SubjectKind = "message", SubjectKey = "already-guid", BodyMd = "x", OwnerUserId = untouchedGuid });
        await db.SaveChangesAsync();

        var updated = await OwnerAuthorUserIdBackfill.RunAsync(db, identity, CancellationToken.None);

        Assert.Equal(0, updated);
        Assert.Equal(0, callCount());

        var stillGuid = await db.Docs.AsNoTracking().FirstAsync(d => d.SubjectKey == "already-guid");
        Assert.Equal(untouchedGuid, stillGuid.OwnerUserId);
        var stillNull = await db.Docs.AsNoTracking().FirstAsync(d => d.SubjectKey == "null-owner");
        Assert.Null(stillNull.OwnerUserId);
    }
}
