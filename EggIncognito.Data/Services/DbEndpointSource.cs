using System.Text;
using EggIncognito.Services;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Data.Services;

// DB-backed endpoint source: looks up stored_endpoints with the same eid-beats-global precedence and
// path-param parent walk as the file source. Scoped, since it depends on the scoped DbContext.
// Priority 100 so the store consults it before the file default.
public sealed class DbEndpointSource(EggIncognitoDbContext db) : IEndpointSource
{
    public int Priority => 100;

    public byte[]? Lookup(string path, string? eid)
    {
        var cleanPath = path.TrimEnd('/');
        while (true)
        {
            if (eid is not null)
            {
                var byEid = db.StoredEndpoints.AsNoTracking()
                    .FirstOrDefault(e => e.Path == cleanPath && e.Eid == eid);
                if (byEid is not null) return Encoding.UTF8.GetBytes(byEid.ResponseJson);
            }
            var global = db.StoredEndpoints.AsNoTracking()
                .FirstOrDefault(e => e.Path == cleanPath && e.Eid == null);
            if (global is not null) return Encoding.UTF8.GetBytes(global.ResponseJson);

            var lastSlash = cleanPath.LastIndexOf('/');
            var firstSlash = cleanPath.IndexOf('/');
            if (lastSlash <= firstSlash) break;
            cleanPath = cleanPath[..lastSlash];
        }
        return null;
    }
}
