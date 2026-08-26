using EggIncognito.Data.Models;
using EggIncognito.Services.Feed;

namespace EggIncognito.Tests;

public class FeedAdminGroupingTests {
    private static FeedSubscription Sub(int id, Guid? owner, bool active = true, string created = "2026-08-01") =>
        new() {
            Id = id,
            Kind = "discord",
            TargetUrl = "https://discord.com/api/webhooks/123456/tokentokentoken",
            EventKind = "proto_build",
            Platforms = ["android", "ios"],
            Trigger = "any",
            Filters = [],
            OwnerUserId = owner,
            Active = active,
            CreatedAt = DateTimeOffset.Parse(created + "T00:00:00Z")
        };

    [Fact]
    public void Build_ResolvesUsernames_AndBucketsUnownedLast() {
        var alice = Guid.NewGuid();
        var result = FeedAdminGrouping.Build(
            [Sub(1, null), Sub(2, alice), Sub(3, Guid.NewGuid())],
            new Dictionary<Guid, string> { [alice] = "alice" });
        Assert.Equal(3, result.Total);
        Assert.Equal("alice", result.Rows[0].OwnerUsername);
        Assert.Equal(FeedAdminGrouping.Unowned, result.Rows[^1].OwnerUsername);
    }

    [Fact]
    public void Build_UnknownOwnerGuid_FallsToUnowned() {
        var result = FeedAdminGrouping.Build([Sub(1, Guid.NewGuid())], new Dictionary<Guid, string>());
        Assert.Equal(FeedAdminGrouping.Unowned, result.Rows[0].OwnerUsername);
    }

    [Fact]
    public void Build_CountsActiveAndOwners() {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var result = FeedAdminGrouping.Build(
            [Sub(1, a), Sub(2, a, active: false), Sub(3, b)],
            new Dictionary<Guid, string> { [a] = "a", [b] = "b" });
        Assert.Equal(2, result.ActiveCount);
        Assert.Equal(2, result.Owners);
    }

    [Fact]
    public void Build_MasksWebhookUrl() {
        var result = FeedAdminGrouping.Build([Sub(1, null)], new Dictionary<Guid, string>());
        Assert.DoesNotContain("tokentokentoken", result.Rows[0].TargetMasked);
        Assert.Contains("...", result.Rows[0].TargetMasked);
    }
}
