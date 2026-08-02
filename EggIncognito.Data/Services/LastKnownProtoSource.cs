using EggIncognito.Services;
using Microsoft.Extensions.DependencyInjection;

namespace EggIncognito.Data.Services;

public sealed class LastKnownProtoSource(IServiceScopeFactory scopeFactory) : ILastKnownProtoSource {
    public async Task<IReadOnlyList<LatestProtoText>> GetLatestProtosAsync(CancellationToken ct = default) {
        using var scope = scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetService<IProtoBackfillStore>();
        if (store is null) return [];
        var rows = await store.LatestProtoTextsAsync(ct);
        return rows.Select(r => new LatestProtoText(r.Platform, r.Build, r.ProtoText)).ToList();
    }
}
