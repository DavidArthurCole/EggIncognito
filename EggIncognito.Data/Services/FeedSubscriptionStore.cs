using EggIncognito.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Data.Services;

// Persistence surface the dispatcher depends on. Extracted so the dispatcher can be faked without EF.
public interface IFeedSubscriptionStore
{
    Task<FeedSubscription> AddAsync(FeedSubscription sub, CancellationToken ct = default);
    Task<List<FeedSubscription>> ActiveAsync(CancellationToken ct = default);
    Task<bool> AlreadyDeliveredAsync(int subId, int protoVersionId, CancellationToken ct = default);
    Task RecordAsync(FeedDelivery delivery, CancellationToken ct = default);
    Task SetActiveAsync(int subId, bool active, CancellationToken ct = default);
    Task BumpFailAsync(int subId, CancellationToken ct = default);
    Task MarkDeliveredAsync(int subId, DateTimeOffset at, CancellationToken ct = default);
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

    public async Task<bool> AlreadyDeliveredAsync(int subId, int protoVersionId, CancellationToken ct = default) =>
        await db.FeedDeliveries.AnyAsync(d => d.SubscriptionId == subId && d.ProtoVersionId == protoVersionId, ct);

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
}
