using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SyncKit.Identity.Client;

namespace EggIncognito.Tools;

// One-shot data migration: populate UserId on capture_proxy_addrs/capture_user_cas rows minted
// before those tables were keyed by user id. Run once via the admin endpoint before applying the
// RepointCaptureTablesToUserId migration, whose Up() guard rejects unbackfilled rows.
public static class CaptureUserIdBackfill
{
    public static async Task<int> RunAsync(EggIncognitoDbContext db, IdentityApiClient identity, CancellationToken ct)
    {
        var updated = 0;
        var addrRows = await db.CaptureProxyAddrs.Where(a => a.UserId == Guid.Empty).ToListAsync(ct);
        foreach (var row in addrRows)
        {
            var result = await identity.ResolveAsync("discord", row.DiscordId!, row.DiscordId, username: null, avatar: null, ct);
            row.UserId = result.UserId;
            updated++;
        }

        var caRows = await db.CaptureUserCas.Where(c => c.UserId == Guid.Empty).ToListAsync(ct);
        foreach (var row in caRows)
        {
            var result = await identity.ResolveAsync("discord", row.DiscordId!, row.DiscordId, username: null, avatar: null, ct);
            row.UserId = result.UserId;
            updated++;
        }

        await db.SaveChangesAsync(ct);
        return updated;
    }
}

// Second one-shot data migration: resolves the Discord-ID strings still held in owner_user_id /
// author_user_id across 7 tables into SyncKit user ids, in place, before RetypeOwnerAuthorUserIdColumns
// alters those columns from text to uuid. Runs over raw SQL (not EF) because the columns are still text
// at this point while the C# models are already Guid?, so an EF query against them would fail to map.
public static class OwnerAuthorUserIdBackfill
{
    private static readonly (string Table, string Column)[] Targets =
    [
        ("docs", "owner_user_id"),
        ("doc_images", "owner_user_id"),
        ("env_designs", "owner_user_id"),
        ("env_design_versions", "author_user_id"),
        ("stored_endpoints", "owner_user_id"),
        ("stored_routes", "owner_user_id"),
        ("feed_subscriptions", "owner_user_id"),
    ];

    public static async Task<int> RunAsync(EggIncognitoDbContext db, IdentityApiClient identity, CancellationToken ct)
    {
        var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);

        var updated = 0;
        // Shared across all 7 tables: the same Discord ID often owns rows in multiple tables, so
        // caching keeps each distinct id resolved via ResolveAsync exactly once for this run.
        var cache = new Dictionary<string, Guid>();

        foreach (var (table, column) in Targets)
        {
            var discordIds = new List<string>();
            await using (var select = new NpgsqlCommand(
                $"SELECT DISTINCT {column} FROM {table} WHERE {column} IS NOT NULL " +
                $"AND {column} !~ '^[0-9a-fA-F]{{8}}-[0-9a-fA-F]{{4}}-[0-9a-fA-F]{{4}}-[0-9a-fA-F]{{4}}-[0-9a-fA-F]{{12}}$'",
                conn))
            await using (var reader = await select.ExecuteReaderAsync(ct))
            {
                while (await reader.ReadAsync(ct))
                    discordIds.Add(reader.GetString(0));
            }

            foreach (var discordId in discordIds)
            {
                if (!cache.TryGetValue(discordId, out var userId))
                {
                    var result = await identity.ResolveAsync("discord", discordId, discordId, username: null, avatar: null, ct);
                    userId = result.UserId;
                    cache[discordId] = userId;
                }

                await using var update = new NpgsqlCommand(
                    $"UPDATE {table} SET {column} = $1 WHERE {column} = $2", conn);
                update.Parameters.AddWithValue(userId.ToString());
                update.Parameters.AddWithValue(discordId);
                updated += await update.ExecuteNonQueryAsync(ct);
            }
        }

        return updated;
    }
}
