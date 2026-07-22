using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text.Json;

namespace EggIncognito.Services;

public static class SupporterClaims
{
    public const string ClaimType = "egi:supporter";

    public static void Stamp(ClaimsIdentity? identity, bool isSupporter)
    {
        if (identity is null) return;
        if (identity.FindFirst(ClaimType) is { } existing) identity.RemoveClaim(existing);
        identity.AddClaim(new Claim(ClaimType, isSupporter ? "true" : "false"));
    }
}
public interface ISupporterStatus
{
    Task<bool> CheckAsync(string discordId, CancellationToken ct = default);
}

public sealed class SupporterStatus(
    IHttpClientFactory httpFactory, IConfiguration config, ILogger<SupporterStatus> logger, TimeProvider clock) : ISupporterStatus
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);
    private readonly ConcurrentDictionary<string, (bool IsSupporter, DateTimeOffset Expires)> _cache = new();

    public static bool ParseHasRole(string memberJson, string roleId)
    {
        try
        {
            using var doc = JsonDocument.Parse(memberJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return false;
            if (!doc.RootElement.TryGetProperty("roles", out var roles)) return false;
            if (roles.ValueKind != JsonValueKind.Array) return false;
            foreach (var r in roles.EnumerateArray())
                if (r.ValueKind == JsonValueKind.String && r.GetString() == roleId) return true;
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public void Invalidate(string discordId) => _cache.TryRemove(discordId, out _);

    public async Task<bool> CheckAsync(string discordId, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        if (_cache.TryGetValue(discordId, out var cached) && cached.Expires > now)
            return cached.IsSupporter;

        var result = await CheckLiveAsync(discordId, ct);
        _cache[discordId] = (result, now + CacheTtl);
        return result;
    }

    private async Task<bool> CheckLiveAsync(string discordId, CancellationToken ct)
    {
        var guildId = config["Discord:GuildId"];
        var roleId = config["Discord:SupporterRoleId"];
        var token = config["Discord:BotToken"];
        if (string.IsNullOrWhiteSpace(guildId) || string.IsNullOrWhiteSpace(roleId)
            || string.IsNullOrWhiteSpace(token))
            return false;

        try
        {
            var http = httpFactory.CreateClient("discord-api");
            using var req = new HttpRequestMessage(HttpMethod.Get,
                $"https://discord.com/api/v10/guilds/{guildId}/members/{discordId}");
            req.Headers.TryAddWithoutValidation("Authorization", $"Bot {token}");
            using var res = await http.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode) return false;
            return ParseHasRole(await res.Content.ReadAsStringAsync(ct), roleId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Supporter check failed for {DiscordId}; fail-closed", discordId);
            return false;
        }
    }
}
