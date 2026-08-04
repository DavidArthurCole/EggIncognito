using System.Text.Json;
using EggIncognito.Core;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.Services;
using EggIncognito.Services.ProtoExtract;
using EggIncognito.Tools;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Tests;

public class ProtoRealignBackfillTests {
    private const string LegacyText = """
        syntax = "proto2";
        package ei;
        message M {
            optional int32 a = 1;
            required string b = 2;
            repeated bool c = 3;
            uint32 d = 4;
        }
        """;

    private const string UnparseableText = """
        syntax = "proto2";
        package ei;
        message M {
        } message N {
            optional int32 b = 1;
        }
        """;

    private static DbContextOptions<EggIncognitoDbContext> Opts =>
        new DbContextOptionsBuilder<EggIncognitoDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=eggincognito_test;Username=x;Password=x;Timeout=1").Options;

    private static string UniqueBuild() => $"realign-{Guid.NewGuid():N}";

    [Fact(Skip = "needs a reachable Postgres")]
    public async Task RunAsync_RegistryLegacyText_RealignsToCanonicalForm() {
        await using var db = new EggIncognitoDbContext(Opts);
        string build = UniqueBuild();
        var version = new ProtoVersion { Platform = "android", Build = build, AppVersion = "1.0", ProtoSha = ProtoHash.Of(LegacyText) };
        db.ProtoVersions.Add(version);
        await db.SaveChangesAsync(CancellationToken.None);
        db.ProtoProtos.Add(new ProtoProto { ProtoVersionId = version.Id, ProtoText = LegacyText, MessageIndex = "[]" });
        await db.SaveChangesAsync(CancellationToken.None);

        try {
            var expected = ProtoCanonicalForm.Normalize(LegacyText);
            string key = $"android/{build}";

            var report = await ProtoRealignBackfill.RunAsync(db, false, CancellationToken.None);

            Assert.Contains(report.Rows, r => r.Kind == "registry" && r.Key == key && r.Status == "updated" && r.NewSha == expected.Sha);

            var pp = await db.ProtoProtos.AsNoTracking().SingleAsync(x => x.ProtoVersionId == version.Id);
            var reloaded = await db.ProtoVersions.AsNoTracking().SingleAsync(x => x.Id == version.Id);
            Assert.Equal(expected.Sha, reloaded.ProtoSha);
            Assert.Equal(expected.Text, pp.ProtoText);
            Assert.Equal(JsonSerializer.Serialize(ProtoTextIndex.Names(expected.Text!)), pp.MessageIndex);
        } finally {
            await db.ProtoProtos.Where(x => x.ProtoVersionId == version.Id).ExecuteDeleteAsync(CancellationToken.None);
            await db.ProtoVersions.Where(x => x.Id == version.Id).ExecuteDeleteAsync(CancellationToken.None);
        }
    }

    [Fact(Skip = "needs a reachable Postgres")]
    public async Task RunAsync_RunTwice_SecondRunIsAlreadyCanonicalNoWrites() {
        await using var db = new EggIncognitoDbContext(Opts);
        string build = UniqueBuild();
        var version = new ProtoVersion { Platform = "android", Build = build, AppVersion = "1.0", ProtoSha = ProtoHash.Of(LegacyText) };
        db.ProtoVersions.Add(version);
        await db.SaveChangesAsync(CancellationToken.None);
        db.ProtoProtos.Add(new ProtoProto { ProtoVersionId = version.Id, ProtoText = LegacyText, MessageIndex = "[]" });
        await db.SaveChangesAsync(CancellationToken.None);

        try {
            string key = $"android/{build}";

            var first = await ProtoRealignBackfill.RunAsync(db, false, CancellationToken.None);
            Assert.Contains(first.Rows, r => r.Key == key && r.Status == "updated");

            var beforeSecond = await db.ProtoVersions.AsNoTracking().SingleAsync(x => x.Id == version.Id);

            var second = await ProtoRealignBackfill.RunAsync(db, false, CancellationToken.None);
            Assert.DoesNotContain(second.Rows, r => r.Key == key);

            var afterSecond = await db.ProtoVersions.AsNoTracking().SingleAsync(x => x.Id == version.Id);
            Assert.Equal(beforeSecond.ProtoSha, afterSecond.ProtoSha);
        } finally {
            await db.ProtoProtos.Where(x => x.ProtoVersionId == version.Id).ExecuteDeleteAsync(CancellationToken.None);
            await db.ProtoVersions.Where(x => x.Id == version.Id).ExecuteDeleteAsync(CancellationToken.None);
        }
    }

    [Fact(Skip = "needs a reachable Postgres")]
    public async Task RunAsync_UnparseableRegistryText_ReportsFailedAndLeavesRowUnchanged() {
        await using var db = new EggIncognitoDbContext(Opts);
        string build = UniqueBuild();
        var version = new ProtoVersion { Platform = "android", Build = build, AppVersion = "1.0", ProtoSha = ProtoHash.Of(UnparseableText) };
        db.ProtoVersions.Add(version);
        await db.SaveChangesAsync(CancellationToken.None);
        db.ProtoProtos.Add(new ProtoProto { ProtoVersionId = version.Id, ProtoText = UnparseableText, MessageIndex = "[]" });
        await db.SaveChangesAsync(CancellationToken.None);

        try {
            string key = $"android/{build}";

            var report = await ProtoRealignBackfill.RunAsync(db, false, CancellationToken.None);

            var failedRow = Assert.Single(report.Rows, r => r.Key == key);
            Assert.Equal("failed", failedRow.Status);
            Assert.NotNull(failedRow.Error);

            var pp = await db.ProtoProtos.AsNoTracking().SingleAsync(x => x.ProtoVersionId == version.Id);
            var reloaded = await db.ProtoVersions.AsNoTracking().SingleAsync(x => x.Id == version.Id);
            Assert.Equal(UnparseableText, pp.ProtoText);
            Assert.Equal(ProtoHash.Of(UnparseableText), reloaded.ProtoSha);
        } finally {
            await db.ProtoProtos.Where(x => x.ProtoVersionId == version.Id).ExecuteDeleteAsync(CancellationToken.None);
            await db.ProtoVersions.Where(x => x.Id == version.Id).ExecuteDeleteAsync(CancellationToken.None);
        }
    }

    [Fact(Skip = "needs a reachable Postgres")]
    public async Task RunAsync_StagedRow_RealignsToCanonicalForm() {
        await using var db = new EggIncognitoDbContext(Opts);
        string build = UniqueBuild();
        var staged = new StagedProto {
            Platform = "android",
            Build = build,
            AppVersion = "1.0",
            ProtoSha = ProtoHash.Of(LegacyText),
            ProtoText = LegacyText,
            MessageIndex = null,
            Status = "pending"
        };
        db.StagedProtos.Add(staged);
        await db.SaveChangesAsync(CancellationToken.None);

        try {
            var expected = ProtoCanonicalForm.Normalize(LegacyText);
            string key = $"staged:{staged.Id}";

            var report = await ProtoRealignBackfill.RunAsync(db, false, CancellationToken.None);

            Assert.Contains(report.Rows, r => r.Kind == "staged" && r.Key == key && r.Status == "updated");

            var reloaded = await db.StagedProtos.AsNoTracking().SingleAsync(x => x.Id == staged.Id);
            Assert.Equal(expected.Sha, reloaded.ProtoSha);
            Assert.Equal(expected.Text, reloaded.ProtoText);
            Assert.Equal(JsonSerializer.Serialize(ProtoTextIndex.Names(expected.Text!)), reloaded.MessageIndex);
        } finally {
            await db.StagedProtos.Where(x => x.Id == staged.Id).ExecuteDeleteAsync(CancellationToken.None);
        }
    }

    [Fact(Skip = "needs a reachable Postgres")]
    public async Task RunAsync_AnalyzedFileWithOldSha_RemappedThroughShaMap() {
        await using var db = new EggIncognitoDbContext(Opts);
        string build = UniqueBuild();
        string fileSha = $"file-{Guid.NewGuid():N}";
        string oldSha = ProtoHash.Of(LegacyText);
        var version = new ProtoVersion { Platform = "android", Build = build, AppVersion = "1.0", ProtoSha = oldSha };
        db.ProtoVersions.Add(version);
        await db.SaveChangesAsync(CancellationToken.None);
        db.ProtoProtos.Add(new ProtoProto { ProtoVersionId = version.Id, ProtoText = LegacyText, MessageIndex = "[]" });
        db.AnalyzedFiles.Add(new AnalyzedFile { FileSha = fileSha, Source = "test", ProtoSha = oldSha });
        await db.SaveChangesAsync(CancellationToken.None);

        try {
            var expected = ProtoCanonicalForm.Normalize(LegacyText);

            var report = await ProtoRealignBackfill.RunAsync(db, false, CancellationToken.None);

            Assert.True(report.RemappedAnalyzedFiles >= 1);
            var af = await db.AnalyzedFiles.AsNoTracking().SingleAsync(x => x.FileSha == fileSha);
            Assert.Equal(expected.Sha, af.ProtoSha);
        } finally {
            await db.AnalyzedFiles.Where(x => x.FileSha == fileSha).ExecuteDeleteAsync(CancellationToken.None);
            await db.ProtoProtos.Where(x => x.ProtoVersionId == version.Id).ExecuteDeleteAsync(CancellationToken.None);
            await db.ProtoVersions.Where(x => x.Id == version.Id).ExecuteDeleteAsync(CancellationToken.None);
        }
    }

    [Fact(Skip = "needs a reachable Postgres")]
    public async Task RunAsync_DryRun_ReportIdenticalShapeDatabaseUnchanged() {
        await using var db = new EggIncognitoDbContext(Opts);
        string build = UniqueBuild();
        string legacySha = ProtoHash.Of(LegacyText);
        var version = new ProtoVersion { Platform = "android", Build = build, AppVersion = "1.0", ProtoSha = legacySha };
        db.ProtoVersions.Add(version);
        await db.SaveChangesAsync(CancellationToken.None);
        db.ProtoProtos.Add(new ProtoProto { ProtoVersionId = version.Id, ProtoText = LegacyText, MessageIndex = "[]" });
        await db.SaveChangesAsync(CancellationToken.None);

        try {
            string key = $"android/{build}";
            var expected = ProtoCanonicalForm.Normalize(LegacyText);

            var dryReport = await ProtoRealignBackfill.RunAsync(db, true, CancellationToken.None);

            var dryRow = Assert.Single(dryReport.Rows, r => r.Key == key);
            Assert.Equal("updated", dryRow.Status);
            Assert.Equal(legacySha, dryRow.OldSha);
            Assert.Equal(expected.Sha, dryRow.NewSha);

            var ppAfterDry = await db.ProtoProtos.AsNoTracking().SingleAsync(x => x.ProtoVersionId == version.Id);
            var versionAfterDry = await db.ProtoVersions.AsNoTracking().SingleAsync(x => x.Id == version.Id);
            Assert.Equal(LegacyText, ppAfterDry.ProtoText);
            Assert.Equal(legacySha, versionAfterDry.ProtoSha);

            var wetReport = await ProtoRealignBackfill.RunAsync(db, false, CancellationToken.None);
            var wetRow = Assert.Single(wetReport.Rows, r => r.Key == key);
            Assert.Equal(dryRow.OldSha, wetRow.OldSha);
            Assert.Equal(dryRow.NewSha, wetRow.NewSha);

            var ppAfterWet = await db.ProtoProtos.AsNoTracking().SingleAsync(x => x.ProtoVersionId == version.Id);
            Assert.Equal(expected.Text, ppAfterWet.ProtoText);
        } finally {
            await db.ProtoProtos.Where(x => x.ProtoVersionId == version.Id).ExecuteDeleteAsync(CancellationToken.None);
            await db.ProtoVersions.Where(x => x.Id == version.Id).ExecuteDeleteAsync(CancellationToken.None);
        }
    }
}
