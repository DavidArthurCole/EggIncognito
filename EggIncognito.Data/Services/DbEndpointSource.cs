using System.Text;
using EggIncognito.Services;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Data.Services;

public sealed class DbEndpointSource(EggIncognitoDbContext db) : IEndpointSource {
    public int Priority => 100;

    public byte[]? Lookup(string path, string? eid) {
        var cleanPath = path.TrimEnd('/');
        while (true) {

            var json = db.StoredEndpoints.AsNoTracking()
                .Where(e => e.Path == cleanPath && (e.Eid == eid || e.Eid == null))
                .OrderBy(e => e.Eid == null ? 1 : 0)
                .Select(e => e.ResponseJson)
                .FirstOrDefault();
            if (json is not null) return Encoding.UTF8.GetBytes(json);

            var lastSlash = cleanPath.LastIndexOf('/');
            var firstSlash = cleanPath.IndexOf('/');
            if (lastSlash <= firstSlash) break;
            cleanPath = cleanPath[..lastSlash];
        }
        return null;
    }
}
