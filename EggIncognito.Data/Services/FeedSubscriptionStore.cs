using EggIncognito.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Data.Services;

public interface IFeedSubscriptionStore
{
    Task<FeedSubscription> AddAsync(FeedSubscription sub, CancellationToken ct = default);
    Task<List<FeedSubscription>> ActiveAsync(CancellationToken ct = default);
    Task<bool> AlreadyDeliveredAsync(int subId, string eventKind, string dedupKey, CancellationToken ct = default);
    Task RecordAsync(FeedDelivery delivery, CancellationToken ct = default);
    Task SetActiveAsync(int subId, bool active, CancellationToken ct = default);
    Task BumpFailAsync(int subId, CancellationToken ct = default);
    Task MarkDeliveredAsync(int subId, DateTimeOffset at, CancellationToken ct = default);
    Task<List<FeedSubscription>> ByOwnerAsync(Guid ownerUserId, CancellationToken ct = default);
    Task<bool> DeleteAsync(int id, Guid ownerUserId, CancellationToken ct = default);
    Task<bool> UpdateAsync(int id, Guid ownerUserId, string[] platforms, string trigger, bool active,
        string? messageTemplate, CancellationToken ct = default);
}

public sealed class FeedSubscriptionStore(EggIncognitoDbContext db) : IFeedSubscriptionStore
{
    public async Task<FeedSubscription> AddAsync(FeedSubscription sub, CancellationToken ct = default)
    {
        db.FeedSubscriptions.Add(sub);
        await db.SaveChangesAsync(ct);
        return sub;
    }

    public Task<List<FeedSubscription>> ActiveAsync(CancellationToken ct = default) =>
        db.FeedSubscriptions.AsNoTracking().Where(s => s.Active).ToListAsync(ct);

    public async Task<bool> AlreadyDeliveredAsync(int subId, string eventKind, string dedupKey, CancellationToken ct = default) =>
        await db.FeedDeliveries.AnyAsync(
            d => d.SubscriptionId == subId && d.EventKind == eventKind && d.DedupKey == dedupKey, ct);

    public async Task RecordAsync(FeedDelivery delivery, CancellationToken ct = default)
    {
        db.FeedDeliveries.Add(delivery);
        await db.SaveChangesAsync(ct);
    }

    public async Task SetActiveAsync(int subId, bool active, CancellationToken ct = default)
    {
        var s = await db.FeedSubscriptions.FirstOrDefaultAsync(x => x.Id == subId, ct);
        if (s is null) return;
        s.Active = active;
        await db.SaveChangesAsync(ct);
    }

    public async Task BumpFailAsync(int subId, CancellationToken ct = default)
    {
        var s = await db.FeedSubscriptions.FirstOrDefaultAsync(x => x.Id == subId, ct);
        if (s is null) return;
        s.FailCount++;
        await db.SaveChangesAsync(ct);
    }

    public async Task MarkDeliveredAsync(int subId, DateTimeOffset at, CancellationToken ct = default)
    {
        var s = await db.FeedSubscriptions.FirstOrDefaultAsync(x => x.Id == subId, ct);
        if (s is null) return;
        s.LastDeliveryAt = at;
        s.FailCount = 0;
        await db.SaveChangesAsync(ct);
    }

    public Task<List<FeedSubscription>> ByOwnerAsync(Guid ownerUserId, CancellationToken ct = default) =>
        db.FeedSubscriptions.AsNoTracking()
            .Where(s => s.OwnerUserId == ownerUserId)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(ct);

    public async Task<bool> DeleteAsync(int id, Guid ownerUserId, CancellationToken ct = default) =>
        await db.FeedSubscriptions
            .Where(s => s.Id == id && s.OwnerUserId == ownerUserId)
            .ExecuteDeleteAsync(ct) > 0;

   
    public async Task<bool> UpdateAsync(
        int id, Guid ownerUserId, string[] platforms, string trigger, bool active, string? messageTemplate,
        CancellationToken ct = default)
    {
        var row = await db.FeedSubscriptions.FirstOrDefaultAsync(s => s.Id == id && s.OwnerUserId == ownerUserId, ct);
        if (row is null) return false;
        row.Platforms = platforms is { Length: > 0 } ? platforms : ["android", "ios"];
        row.Trigger = trigger == "new_version" ? "new_version" : "proto_changed";
        row.Active = active;
        row.MessageTemplate = string.IsNullOrWhiteSpace(messageTemplate) ? null : messageTemplate;
        await db.SaveChangesAsync(ct);
        return true;
    }
}
