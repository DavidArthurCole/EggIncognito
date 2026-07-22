using EggIncognito.Data.Models;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Data.Services;

public class EggIncognitoDbContext(DbContextOptions<EggIncognitoDbContext> options)
    : DbContext(options), IDataProtectionKeyContext {
    public DbSet<StoredEndpoint> StoredEndpoints => Set<StoredEndpoint>();
    public DbSet<StoredRoute> StoredRoutes => Set<StoredRoute>();
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
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<DeviceProbe> DeviceProbes => Set<DeviceProbe>();
    public DbSet<DeviceUpdate> DeviceUpdates => Set<DeviceUpdate>();
    public DbSet<StagedProto> StagedProtos => Set<StagedProto>();
    public DbSet<StoredMesh> StoredMeshes => Set<StoredMesh>();
    public DbSet<StoredIcon> StoredIcons => Set<StoredIcon>();
    public DbSet<EnvDesign> EnvDesigns => Set<EnvDesign>();
    public DbSet<EnvDesignVersion> EnvDesignVersions => Set<EnvDesignVersion>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder b) {
        b.Entity<StoredEndpoint>(e => {
            e.HasIndex(x => new { x.Path, x.Eid }).IsUnique();
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
            e.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");
        });
        b.Entity<StoredRoute>(r => {
            r.HasIndex(x => x.Path).IsUnique();

            r.HasIndex(x => x.Source);
            r.Property(x => x.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
        });
        b.Entity<Doc>(d => {
            d.HasIndex(x => new { x.SubjectKind, x.SubjectKey }).IsUnique();
            d.Property(x => x.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
            d.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");
        });
        b.Entity<Tag>(t => t.HasIndex(x => x.Slug).IsUnique());
        b.Entity<SubjectTag>(s => s.HasIndex(x => new { x.SubjectKind, x.SubjectKey, x.TagId }).IsUnique());
        b.Entity<DocImage>(im => im.Property(x => x.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd());
        b.Entity<CaptureUserCa>(c => c.Property(x => x.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd());
        b.Entity<CaptureProxyAddr>(a => {
            a.HasIndex(x => x.Addr).IsUnique();
            a.Property(x => x.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
        });
        b.Entity<ProtoVersion>(e => {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.Platform, x.Build }).IsUnique();
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
        });
        b.Entity<ProtoProto>(e => {
            e.HasKey(x => x.ProtoVersionId);
            e.Property(x => x.MessageIndex).HasColumnType("jsonb");
        });
        b.Entity<FeedSubscription>(e => {
            e.HasKey(x => x.Id);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
            e.Property(x => x.Platforms).HasColumnType("text[]");
            e.Property(x => x.EventKind).HasDefaultValue("proto_build");
        });
        b.Entity<FeedDelivery>(e => {
            e.HasKey(x => x.Id);
            e.Property(x => x.EventKind).HasDefaultValue("proto_build");
            e.Property(x => x.DedupKey).HasDefaultValue("");
            e.HasIndex(x => new { x.SubscriptionId, x.EventKind, x.DedupKey }).IsUnique();
        });
        b.Entity<BackfillJob>(e => {
            e.HasKey(x => x.Id);

            e.HasIndex(x => new { x.Source, x.StartedAt });
        });
        b.Entity<KnownVersion>(e => {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.Platform, x.AppVersion, x.Source }).IsUnique();
            e.Property(x => x.FirstSeen).HasDefaultValueSql("now()");
        });
        b.Entity<ExtractJob>(e => {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.Platform, x.AppVersion }).IsUnique();
        });
        b.Entity<Device>(e => {
            e.HasKey(x => x.Id);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
        });
        b.Entity<DeviceProbe>(e => {
            e.HasKey(x => x.Id);

            e.HasIndex(x => new { x.DeviceId, x.ProbedAt });
        });
        b.Entity<DeviceUpdate>(e => {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.DeviceId, x.AttemptedAt });
        });
        b.Entity<StagedProto>(e => {
            e.HasIndex(x => x.ProtoSha);
            e.HasIndex(x => x.Status);
        });
        b.Entity<StoredMesh>(e => {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.Platform, x.Stem }).IsUnique();
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
        });
        b.Entity<StoredIcon>(e => {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
        });
        b.Entity<EnvDesign>(e => {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
            e.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");
        });
        b.Entity<EnvDesignVersion>(e => {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.DesignId, x.VersionNo }).IsUnique();
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
        });
        b.Entity<ApiKey>(e => {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.KeyHash).IsUnique();
            e.HasIndex(x => x.OwnerUserId);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
        });
    }
}
