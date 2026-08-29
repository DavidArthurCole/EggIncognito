using System.Text.Json;
using System.Text.Json.Nodes;
using EggIncognito.Capture;
using EggIncognito.Core;
using EggIncognito.Core.Services;
using EggIncognito.Models.Observations;
using EggIncognito.Services.Devices;
using Ei;
using Google.Protobuf;

namespace EggIncognito.Services.Contributions;

public sealed class ArtifactContributionKind : ICaptureContributionKind {
    public const string KindName = "artifact-observation";

    private static readonly JsonSerializerOptions CamelJson = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public string Kind => KindName;

    public IReadOnlyCollection<string> Routes { get; } = [
        ConsumeObservationRecorder.CraftRoute,
        ConsumeObservationRecorder.ConsumeRoute,
        ConsumeObservationRecorder.DemoteRoute
    ];

    public ContributionDraft? Build(DashboardFlow flow) {
        string? action = ConsumeObservationRecorder.ActionFor(flow.Path);
        if (action is null || flow.RequestJsonRaw is null || flow.ResponseJsonRaw is null) return null;

        var payload = string.Equals(action, "craft", StringComparison.Ordinal)
            ? BuildCraft(flow)
            : BuildConsume(action, flow);
        if (payload is null) return null;

        string json = payload.Payload.ToJsonString(CamelJson);
        string hash = Hashes.Sha256Hex($"{KindName}|{flow.Id}|{flow.Timestamp}|{json}");
        return new ContributionDraft(KindName, payload.Summary, json, hash, payload.ClientVersion);
    }

    private static ContributionBody? BuildCraft(DashboardFlow flow) {
        var request = JsonParser.Default.Parse<CraftArtifactRequest>(flow.RequestJsonRaw!);
        var response = JsonParser.Default.Parse<CraftArtifactResponse>(flow.ResponseJsonRaw!);
        if (request.Spec is null) return null;

        (string name, string level, string rarity) = SpecTriple(request.Spec);
        string achieved = ProtoEnumNames.RarityName(response.RarityAchieved);
        var payload = new JsonObject {
            ["action"] = "craft",
            ["spec"] = Spec(name, level, rarity),
            ["countRequested"] = 1,
            ["rarityAchieved"] = achieved,
            ["goldPricePaid"] = request.GoldPricePaid,
            ["craftingCount"] = (int)request.CraftingCount,
            ["success"] = response.ItemId != 0
        };

        return new ContributionBody(payload,
            $"craft {name} {level} {rarity} -> {achieved}",
            Version(request.Rinfo));
    }

    private static ContributionBody? BuildConsume(string action, DashboardFlow flow) {
        var request = JsonParser.Default.Parse<ConsumeArtifactRequest>(flow.RequestJsonRaw!);
        var response = JsonParser.Default.Parse<ConsumeArtifactResponse>(flow.ResponseJsonRaw!);
        if (request.Spec is null) return null;

        (string name, string level, string rarity) = SpecTriple(request.Spec);
        int count = (int)Math.Max(request.Quantity, 1);

        var byproducts = response.Byproducts
            .GroupBy(b => (b.Name, b.Level, b.Rarity))
            .Select(g => new ArtifactByproductRow(ProtoEnumNames.SpecName(g.Key.Name),
                ProtoEnumNames.LevelName(g.Key.Level), ProtoEnumNames.RarityName(g.Key.Rarity), g.Count()))
            .OrderBy(b => b.Name, StringComparer.Ordinal)
            .ThenBy(b => b.Level, StringComparer.Ordinal)
            .ThenBy(b => b.Rarity, StringComparer.Ordinal)
            .ToArray();

        var rewards = response.OtherRewards
            .Select(r => new ArtifactRewardRow(ProtoEnumNames.RewardTypeName(r.RewardType),
                string.IsNullOrEmpty(r.RewardSubType) ? null : r.RewardSubType, r.RewardAmount))
            .ToArray();

        double goldenEggs = response.OtherRewards
            .Where(r => r.RewardType == RewardType.Gold)
            .Sum(r => r.RewardAmount);

        var payload = new JsonObject {
            ["action"] = action,
            ["spec"] = Spec(name, level, rarity),
            ["countRequested"] = count,
            ["byproducts"] = JsonNode.Parse(JsonSerializer.Serialize(byproducts, CamelJson)),
            ["otherRewards"] = JsonNode.Parse(JsonSerializer.Serialize(rewards, CamelJson)),
            ["goldenEggs"] = goldenEggs,
            ["success"] = response.Success
        };

        return new ContributionBody(payload,
            $"{action} {name} {level} {rarity} x{count}",
            Version(request.Rinfo));
    }

    private static JsonObject Spec(string name, string level, string rarity) => new() {
        ["name"] = name,
        ["level"] = level,
        ["rarity"] = rarity
    };

    private static (string Name, string Level, string Rarity) SpecTriple(ArtifactSpec spec) =>
        (ProtoEnumNames.SpecName(spec.Name),
            ProtoEnumNames.LevelName(spec.Level),
            ProtoEnumNames.RarityName(spec.Rarity));

    private static string? Version(BasicRequestInfo? rinfo) =>
        string.IsNullOrEmpty(rinfo?.Version) ? null : rinfo.Version;

    private sealed record ContributionBody(JsonObject Payload, string Summary, string? ClientVersion);
}
