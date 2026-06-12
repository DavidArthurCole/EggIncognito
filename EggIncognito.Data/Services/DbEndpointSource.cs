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
            // One round-trip per segment: fetch the eid and global candidates together, eid match
            // ranked first (eid beats global), projecting just the response json.
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
