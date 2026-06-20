using System.IO.Compression;
using System.Text;
using System.Text.Json;
using EggIncognito.Core.Services.Protos;
using Xunit;

namespace EggIncognito.Tests.ProtoStaging;

public class CrawlManifestReaderTests
{
    static byte[] BuildZip((string name, string content)[] entries)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
            foreach (var (name, content) in entries)
            {
                var e = zip.CreateEntry(name);
                using var w = new StreamWriter(e.Open());
                w.Write(content);
            }
        return ms.ToArray();
    }

    [Fact]
    public void Read_DedupsByProtoSha_SubjectVersionTrusted_EmptyVersionDropped()
    {
        var manifest = JsonSerializer.Serialize(new[]
        {
            // shaA: a subject record (trusted version) + a later tree-scan dup -> subject wins, version kept.
            new { Repo = "r1", Commit = "c1", Date = "2021-01-01T00:00:00Z", ProtoPath = "ei.proto",
                  ProtoSha256 = "shaA", ClientVersion = (int?)40, AppVersion = "1.21", Build = "111",
                  SnapshotFile = "snapshots/r1/a.proto", Reason = "x", VersionConfidence = "subject", CommitSubject = "s" },
            new { Repo = "r2", Commit = "c2", Date = "2022-01-01T00:00:00Z", ProtoPath = "ei.proto",
                  ProtoSha256 = "shaA", ClientVersion = (int?)40, AppVersion = "9.9", Build = "999",
                  SnapshotFile = "snapshots/r2/b.proto", Reason = "x", VersionConfidence = "tree-scan", CommitSubject = "s" },
            // shaB: only an empty-confidence record with a version -> version must be DROPPED (review-only).
            new { Repo = "r3", Commit = "c3", Date = "2021-06-01T00:00:00Z", ProtoPath = "ei.proto",
                  ProtoSha256 = "shaB", ClientVersion = (int?)50, AppVersion = "1.20.3", Build = "110",
                  SnapshotFile = "snapshots/r3/c.proto", Reason = "x", VersionConfidence = "", CommitSubject = "s" },
        });
        var zip = BuildZip(new[]
        {
            ("manifest.json", manifest),
            ("snapshots/r1/a.proto", "syntax = \"proto2\"; // A"),
            ("snapshots/r3/c.proto", "syntax = \"proto2\"; // C"),
        });

        var recs = CrawlManifestReader.Read(zip);

        Assert.Equal(2, recs.Count); // shaA + shaB
        var a = Assert.Single(recs, r => r.ProtoSha == "shaA");
        Assert.Equal("r1", a.OriginRepo);            // subject record chosen
        Assert.Equal("subject", a.Confidence);
        Assert.Equal("1.21", a.AppVersion);           // trusted version attached
        Assert.Equal("111", a.Build);
        Assert.Equal("40", a.ClientVersion);
        Assert.Contains("// A", a.ProtoText);
        Assert.Equal("android", a.Platform);

        var b = Assert.Single(recs, r => r.ProtoSha == "shaB");
        Assert.Null(b.AppVersion);                    // empty confidence -> version dropped
        Assert.Null(b.Build);
        Assert.Null(b.ClientVersion);
        Assert.Null(b.Confidence);                    // empty normalized to null
        Assert.Contains("// C", b.ProtoText);
    }

    [Fact]
    public void Read_TreeScanVersion_NotAttached_ButConfidenceCarried()
    {
        // A single tree-scan record: proto content ingested, but its (heuristic) version is NOT trusted.
        var manifest = JsonSerializer.Serialize(new[]
        {
            new { Repo = "r", Commit = "c", Date = "2024-01-01T00:00:00Z", ProtoPath = "ei.proto",
                  ProtoSha256 = "shaT", ClientVersion = (int?)72, AppVersion = "1.33", Build = "131",
                  SnapshotFile = "snapshots/r/t.proto", Reason = "x", VersionConfidence = "tree-scan", CommitSubject = "s" },
        });
        var zip = BuildZip(new[]
        {
            ("manifest.json", manifest),
            ("snapshots/r/t.proto", "syntax = \"proto2\"; // T"),
        });

        var rec = Assert.Single(CrawlManifestReader.Read(zip));
        Assert.Equal("shaT", rec.ProtoSha);
        Assert.Contains("// T", rec.ProtoText);       // content always ingested
        Assert.Null(rec.AppVersion);                  // tree-scan version NOT attached
        Assert.Null(rec.Build);
        Assert.Null(rec.ClientVersion);
        Assert.Equal("tree-scan", rec.Confidence);    // hint carried for the reviewer
    }

    [Fact]
    public void Read_SkipsRecordsWithMissingSnapshot()
    {
        var manifest = JsonSerializer.Serialize(new[]
        {
            new { Repo = "r", Commit = "c", Date = "2021-01-01T00:00:00Z", ProtoPath = "ei.proto",
                  ProtoSha256 = "shaX", ClientVersion = (int?)null, AppVersion = (string?)null, Build = (string?)null,
                  SnapshotFile = "snapshots/missing.proto", Reason = "x", VersionConfidence = "", CommitSubject = "s" },
        });
        var zip = BuildZip(new[] { ("manifest.json", manifest) }); // no snapshot entry
        Assert.Empty(CrawlManifestReader.Read(zip));
    }
}
