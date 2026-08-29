using EggIncognito.Core.Services;
using EggIncognito.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EggIncognito.Data.Services;

public static class RouteOverrideFetch {
    public static IReadOnlyDictionary<string, RouteOverrideInfo> All(IServiceScopeFactory scopes) {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EggIncognitoDbContext>();
        var rows = db.RouteOverrides.AsNoTracking().Select(ToInfo).ToList();
        return rows.ToDictionary(o => o.Path, StringComparer.Ordinal);
    }

    private static RouteOverrideInfo ToInfo(RouteOverride r) => new(
        r.Path, r.RequestType, r.ResponseType, r.RequestWrapped, r.ResponseWrapped,
        r.PathParam, r.UpdatedAt, r.UpdatedBy);
}
