using System.IO.Compression;
using System.Text.Json;
using EggIncognito.Core.Services.Protos;

namespace EggIncognito.Tests.Protos;

public class CrawlManifestReaderTests {
    private static byte[] BuildZip((string name, string content)[] entries) {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true)) {
            foreach ((string name, string content) in entries) {
                var e = zip.CreateEntry(name);
                using var w = new StreamWriter(e.Open());
                w.Write(content);
            }
        }

        return ms.ToArray();
    }

    [Fact]
    public void Read_DedupsByProtoSha_SubjectVersionTrusted_EmptyVersionDropped() {
        string manifest = JsonSerializer.Serialize(new[] {
            new {
                Repo = "r1", Commit = "c1", Date = "2021-01-01T00:00:00Z", ProtoPath = "ei.proto",
                ProtoSha256 = "shaA", ClientVersion = (int?)40, AppVersion = "1.21", Build = "111",
                SnapshotFile = "snapshots/r1/a.proto", Reason = "x", VersionConfidence = "subject", CommitSubject = "s"
            },
            new {
                Repo = "r2", Commit = "c2", Date = "2022-01-01T00:00:00Z", ProtoPath = "ei.proto",
                ProtoSha256 = "shaA", ClientVersion = (int?)40, AppVersion = "9.9", Build = "999",
                SnapshotFile = "snapshots/r2/b.proto", Reason = "x", VersionConfidence = "tree-scan",
                CommitSubject = "s"
            },

            new {
                Repo = "r3", Commit = "c3", Date = "2021-06-01T00:00:00Z", ProtoPath = "ei.proto",
                ProtoSha256 = "shaB", ClientVersion = (int?)50, AppVersion = "1.20.3", Build = "110",
                SnapshotFile = "snapshots/r3/c.proto", Reason = "x", VersionConfidence = "", CommitSubject = "s"
            }
        });
        byte[] zip = BuildZip(
        [
            ("manifest.json", manifest),
            ("snapshots/r1/a.proto", "syntax = \"proto2\"; // A"),
            ("snapshots/r3/c.proto", "syntax = \"proto2\"; // C")
        ]);

        var recs = CrawlManifestReader.Read(zip);

        Assert.Equal(2, recs.Count);
        var a = Assert.Single(recs, r => r.ProtoSha == "shaA");
        Assert.Equal("r1", a.OriginRepo);
        Assert.Equal("subject", a.Confidence);
        Assert.Equal("1.21", a.AppVersion);
        Assert.Equal("111", a.Build);
        Assert.Equal("40", a.ClientVersion);
        Assert.Contains("// A", a.ProtoText);
        Assert.Equal("android", a.Platform);

        var b = Assert.Single(recs, r => r.ProtoSha == "shaB");
        Assert.Null(b.AppVersion);
        Assert.Null(b.Build);
        Assert.Null(b.ClientVersion);
        Assert.Null(b.Confidence);
        Assert.Contains("// C", b.ProtoText);
    }

    [Fact]
    public void Read_VersionFile_HighestTrust_CarriesPlatformAndFullTriple() {
        string manifest = JsonSerializer.Serialize(new[] {
            new {
                Repo = "subj", Commit = "c1", Date = "2022-01-01T00:00:00Z", ProtoPath = "ei.proto",
                ProtoSha256 = "shaV", ClientVersion = (int?)null, AppVersion = "1.23", Build = (string?)null,
                SnapshotFile = "snapshots/subj/a.proto", Reason = "x", VersionConfidence = "subject",
                CommitSubject = "s", Platform = (string?)null
            },
            new {
                Repo = "vfile", Commit = "c2", Date = "2022-02-01T00:00:00Z", ProtoPath = "ei.proto",
                ProtoSha256 = "shaV", ClientVersion = (int?)40, AppVersion = "1.23.1", Build = (string?)"1.23.1.0",
                SnapshotFile = "snapshots/vfile/b.proto", Reason = "x", VersionConfidence = "version-file",
                CommitSubject = "s", Platform = (string?)"IOS"
            }
        });
        byte[] zip = BuildZip(
        [
            ("manifest.json", manifest),
            ("snapshots/vfile/b.proto", "syntax = \"proto2\"; // V")
        ]);

        var rec = Assert.Single(CrawlManifestReader.Read(zip));
        Assert.Equal("version-file", rec.Confidence);
        Assert.Equal("vfile", rec.OriginRepo);
        Assert.Equal("1.23.1", rec.AppVersion);
        Assert.Equal("1.23.1.0", rec.Build);
        Assert.Equal("40", rec.ClientVersion);
        Assert.Equal("ios", rec.Platform);
        Assert.Contains("// V", rec.ProtoText);
    }

    [Fact]
    public void Read_AndroidPlatform_Normalized() {
        string manifest = JsonSerializer.Serialize(new[] {
            new {
                Repo = "r", Commit = "c", Date = "2025-01-01T00:00:00Z", ProtoPath = "ei.proto",
                ProtoSha256 = "shaD", ClientVersion = (int?)71, AppVersion = "1.17.0", Build = (string?)null,
                SnapshotFile = "snapshots/r/d.proto", Reason = "x", VersionConfidence = "version-file",
                CommitSubject = "s", Platform = "ANDROID"
            }
        });
        byte[] zip = BuildZip([("manifest.json", manifest), ("snapshots/r/d.proto", "syntax = \"proto2\";")]);
        var rec = Assert.Single(CrawlManifestReader.Read(zip));
        Assert.Equal("android", rec.Platform);
        Assert.Equal("1.17.0", rec.AppVersion);
        Assert.Equal("71", rec.ClientVersion);
    }

    [Fact]
    public void Read_TreeScanVersion_NotAttached_ButConfidenceCarried() {
        string manifest = JsonSerializer.Serialize(new[] {
            new {
                Repo = "r", Commit = "c", Date = "2024-01-01T00:00:00Z", ProtoPath = "ei.proto",
                ProtoSha256 = "shaT", ClientVersion = (int?)72, AppVersion = "1.33", Build = "131",
                SnapshotFile = "snapshots/r/t.proto", Reason = "x", VersionConfidence = "tree-scan", CommitSubject = "s"
            }
        });
        byte[] zip = BuildZip(
        [
            ("manifest.json", manifest),
            ("snapshots/r/t.proto", "syntax = \"proto2\"; // T")
        ]);

        var rec = Assert.Single(CrawlManifestReader.Read(zip));
        Assert.Equal("shaT", rec.ProtoSha);
        Assert.Contains("// T", rec.ProtoText);
        Assert.Null(rec.AppVersion);
        Assert.Null(rec.Build);
        Assert.Null(rec.ClientVersion);
        Assert.Equal("tree-scan", rec.Confidence);
    }

    [Fact]
    public void Read_OriginDate_NormalizedToUtc() {
        string manifest = JsonSerializer.Serialize(new[] {
            new {
                Repo = "r", Commit = "c", Date = "2022-02-04T12:59:44-08:00", ProtoPath = "ei.proto",
                ProtoSha256 = "shaTz", ClientVersion = (int?)null, AppVersion = (string?)null, Build = (string?)null,
                SnapshotFile = "snapshots/r/tz.proto", Reason = "x", VersionConfidence = "", CommitSubject = "s"
            }
        });
        byte[] zip = BuildZip(
        [
            ("manifest.json", manifest),
            ("snapshots/r/tz.proto", "syntax = \"proto2\";")
        ]);

        var rec = Assert.Single(CrawlManifestReader.Read(zip));
        Assert.NotNull(rec.OriginDate);
        Assert.Equal(TimeSpan.Zero, rec.OriginDate!.Value.Offset);
        Assert.Equal(new DateTimeOffset(2022, 2, 4, 20, 59, 44, TimeSpan.Zero), rec.OriginDate);
    }

    [Fact]
    public void Read_SkipsRecordsWithMissingSnapshot() {
        string manifest = JsonSerializer.Serialize(new[] {
            new {
                Repo = "r", Commit = "c", Date = "2021-01-01T00:00:00Z", ProtoPath = "ei.proto",
                ProtoSha256 = "shaX", ClientVersion = (int?)null, AppVersion = (string?)null, Build = (string?)null,
                SnapshotFile = "snapshots/missing.proto", Reason = "x", VersionConfidence = "", CommitSubject = "s"
            }
        });
        byte[] zip = BuildZip([("manifest.json", manifest)]);
        Assert.Empty(CrawlManifestReader.Read(zip));
    }
}
