using System.Data;
using EggIdentity.Client;
using EggIncognito.Data.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EggIncognito.Tools;

public static class CaptureUserIdBackfill {
    public static async Task<int> RunAsync(EggIncognitoDbContext db, IdentityApiClient identity, CancellationToken ct) {
        int updated = 0;
        var addrRows = await db.CaptureProxyAddrs.Where(a => a.UserId == Guid.Empty).ToListAsync(ct);
        foreach (var row in addrRows) {
            var result = await identity.ResolveAsync("discord", row.DiscordId!, row.DiscordId, null, null, ct);
            row.UserId = result.UserId;
            updated++;
        }

        var caRows = await db.CaptureUserCas.Where(c => c.UserId == Guid.Empty).ToListAsync(ct);
        foreach (var row in caRows) {
            var result = await identity.ResolveAsync("discord", row.DiscordId!, row.DiscordId, null, null, ct);
            row.UserId = result.UserId;
            updated++;
        }

        await db.SaveChangesAsync(ct);
        return updated;
    }
}

public static class OwnerAuthorUserIdBackfill {
    private static readonly (string Table, string Column)[] Targets = [
        ("docs", "owner_user_id"),
        ("doc_images", "owner_user_id"),
        ("env_designs", "owner_user_id"),
        ("env_design_versions", "author_user_id"),
        ("stored_endpoints", "owner_user_id"),
        ("stored_routes", "owner_user_id"),
        ("feed_subscriptions", "owner_user_id")
    ];

    public static async Task<int> RunAsync(EggIncognitoDbContext db, IdentityApiClient identity, CancellationToken ct) {
        var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

        int updated = 0;


        var cache = new Dictionary<string, Guid>();

        foreach ((string table, string column) in Targets) {
            var discordIds = new List<string>();
            await using (var select = new NpgsqlCommand(
                             $"SELECT DISTINCT {column} FROM {table} WHERE {column} IS NOT NULL " +
                             $"AND {column} !~ '^[0-9a-fA-F]{{8}}-[0-9a-fA-F]{{4}}-[0-9a-fA-F]{{4}}-[0-9a-fA-F]{{4}}-[0-9a-fA-F]{{12}}$'",
                             conn))
            await using (var reader = await select.ExecuteReaderAsync(ct)) {
                while (await reader.ReadAsync(ct))
                    discordIds.Add(reader.GetString(0));
            }

            foreach (string discordId in discordIds) {
                if (!cache.TryGetValue(discordId, out var userId)) {
                    var result = await identity.ResolveAsync("discord", discordId, discordId, null, null, ct);
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
