using EggIncognito.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Data.Services;

public class EggIncognitoDbContext(DbContextOptions<EggIncognitoDbContext> options) : DbContext(options)
{
    public DbSet<StoredEndpoint> StoredEndpoints => Set<StoredEndpoint>();
    public DbSet<StoredRoute> StoredRoutes => Set<StoredRoute>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Doc> Docs => Set<Doc>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<SubjectTag> SubjectTags => Set<SubjectTag>();
    public DbSet<DocImage> DocImages => Set<DocImage>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<StoredEndpoint>(e =>
        {
            e.HasIndex(x => new { x.Path, x.Eid }).IsUnique();
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
            e.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");
        });
        b.Entity<StoredRoute>(r =>
        {
            r.HasIndex(x => x.Path).IsUnique();
            r.Property(x => x.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
        });
        b.Entity<User>(u =>
        {
            u.Property(x => x.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
            u.Property(x => x.Role).HasDefaultValue("viewer");
        });
        b.Entity<Doc>(d =>
        {
            d.HasIndex(x => new { x.SubjectKind, x.SubjectKey }).IsUnique();
            d.Property(x => x.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
            d.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");
        });
        b.Entity<Tag>(t =>
        {
            t.HasIndex(x => x.Slug).IsUnique();
        });
        b.Entity<SubjectTag>(s =>
        {
            s.HasIndex(x => new { x.SubjectKind, x.SubjectKey, x.TagId }).IsUnique();
            s.HasIndex(x => new { x.SubjectKind, x.SubjectKey });
        });
        b.Entity<DocImage>(im =>
        {
            im.Property(x => x.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
        });
    }
}
