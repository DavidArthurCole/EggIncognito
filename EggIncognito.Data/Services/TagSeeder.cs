using EggIncognito.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Data.Services;

public static class TagSeeder
{
    public static readonly (string Slug, string Label, string Color)[] Defaults =
    [
        ("coops", "Coops", "#ef7559"),
        ("contracts", "Contracts", "#5aa9e6"),
        ("artifacts", "Artifacts", "#b88be6"),
        ("shells", "Shells", "#5ec27e"),
        ("backups", "Backups", "#e0c15f"),
        ("missions", "Missions", "#e06f9c"),
        ("eggs", "Eggs & Boosts", "#6fd0e0"),
        ("account", "Account", "#9a9aa5"),
        ("misc", "Misc", "#7f7f8a"),
    ];

    public static async Task SeedAsync(EggIncognitoDbContext db, CancellationToken ct = default)
    {
        var have = await db.Tags.Select(t => t.Slug).ToListAsync(ct);
        var haveSet = new HashSet<string>(have, StringComparer.OrdinalIgnoreCase);
        foreach (var (slug, label, color) in Defaults)
        {
            if (haveSet.Contains(slug)) continue;
            db.Tags.Add(new Tag { Slug = slug, Label = label, Color = color });
        }
        await db.SaveChangesAsync(ct);
    }
}
