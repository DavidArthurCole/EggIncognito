using EggIncognito.Data.Models;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Data.Services;

// Implements IDataProtectionKeyContext so the cookie/OAuth key ring is stored in Postgres; otherwise
// the keys are ephemeral per-process and every restart invalidates existing auth cookies (logging users
// out). DbSet name is the EF-conventional DataProtectionKeys table.
public class EggIncognitoDbContext(DbContextOptions<EggIncognitoDbContext> options)
    : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<StoredEndpoint> StoredEndpoints => Set<StoredEndpoint>();
    public DbSet<StoredRoute> StoredRoutes => Set<StoredRoute>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Doc> Docs => Set<Doc>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<SubjectTag> SubjectTags => Set<SubjectTag>();
    public DbSet<DocImage> DocImages => Set<DocImage>();
    public DbSet<CaptureUserCa> CaptureUserCas => Set<CaptureUserCa>();
    public DbSet<CaptureProxyAddr> CaptureProxyAddrs => Set<CaptureProxyAddr>();
    public DbSet<ProtoVersion> ProtoVersions => Set<ProtoVersion>();
    public DbSet<ProtoProto> ProtoProtos => Set<ProtoProto>();
    public DbSet<FeedSubscription> FeedSubscriptions => Set<FeedSubscription>();
    public DbSet<FeedDelivery> FeedDeliveries => Set<FeedDelivery>();
    public DbSet<BackfillJob> BackfillJobs => Set<BackfillJob>();
    public DbSet<KnownVersion> KnownVersions => Set<KnownVersion>();
    public DbSet<ExtractJob> ExtractJobs => Set<ExtractJob>();
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

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
            // DbRouteProvider.AllDbRoutes filters on source alone; without this it full-scans.
            r.HasIndex(x => x.Source);
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
            // The composite unique index also serves (subject_kind, subject_key) prefix lookups.
            s.HasIndex(x => new { x.SubjectKind, x.SubjectKey, x.TagId }).IsUnique();
        });
        b.Entity<DocImage>(im =>
        {
            im.Property(x => x.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
        });
        b.Entity<CaptureUserCa>(c =>
        {
            c.Property(x => x.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
        });
        b.Entity<CaptureProxyAddr>(a =>
        {
            a.HasIndex(x => x.Addr).IsUnique();
            a.Property(x => x.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
        });
        b.Entity<ProtoVersion>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.Platform, x.Build }).IsUnique();
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
        });
        b.Entity<ProtoProto>(e =>
        {
            e.HasKey(x => x.ProtoVersionId);
            e.Property(x => x.MessageIndex).HasColumnType("jsonb");
        });
        b.Entity<FeedSubscription>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
            e.Property(x => x.Platforms).HasColumnType("text[]");
        });
        b.Entity<FeedDelivery>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.SubscriptionId, x.ProtoVersionId }).IsUnique();
        });
        b.Entity<BackfillJob>(e =>
        {
            e.HasKey(x => x.Id);
            // LatestPerSource orders by StartedAt within a source; index keeps that off a full scan.
            e.HasIndex(x => new { x.Source, x.StartedAt });
        });
        b.Entity<KnownVersion>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.Platform, x.AppVersion, x.Source }).IsUnique();
            e.Property(x => x.FirstSeen).HasDefaultValueSql("now()");
        });
        b.Entity<ExtractJob>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.Platform, x.AppVersion }).IsUnique();
        });
    }
}
