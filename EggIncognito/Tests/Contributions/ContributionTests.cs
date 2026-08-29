using System.Text.Json.Nodes;
using EggIncognito.Capture;
using EggIncognito.Data.Models;
using EggIncognito.Services.Contributions;
using Ei;
using Google.Protobuf;

namespace EggIncognito.Tests.Contributions;

public class ContributionTests {
    private const string Eid = "EI1234567890123456";

    [Fact]
    public void Project_LeavesContributionRoutesUntouched() {
        var allowed = new HashSet<string>(StringComparer.Ordinal) { "ei_afx/craft_artifact" };
        var flow = Flow("ei_afx/craft_artifact");

        var projected = LimitedFlowProjector.Project(flow, allowed);

        Assert.Same(flow, projected);
    }

    [Fact]
    public void Project_StripsEveryPayloadFieldFromOtherRoutes() {
        var allowed = new HashSet<string>(StringComparer.Ordinal) { "ei_afx/craft_artifact" };
        var flow = Flow("ei/first_contact");

        var projected = LimitedFlowProjector.Project(flow, allowed);

        Assert.Null(projected.RequestJson);
        Assert.Null(projected.ResponseJson);
        Assert.Null(projected.RequestJsonRaw);
        Assert.Null(projected.ResponseJsonRaw);
        Assert.Null(projected.RequestDataB64);
        Assert.Null(projected.ResponseText);
        Assert.Null(projected.RequestHeaders);
        Assert.Null(projected.ResponseHeaders);
        Assert.Null(projected.RequestHeadersRaw);
        Assert.Null(projected.ResponseHeadersRaw);
        Assert.Equal("", projected.ResponseB64);
        Assert.Equal("", projected.Url);
    }

    [Fact]
    public void Project_KeepsOnlyWhatTheRowNeedsToRender() {
        var flow = Flow("ei/first_contact");

        var projected = LimitedFlowProjector.Project(flow, new HashSet<string>(StringComparer.Ordinal));

        Assert.Equal(flow.Id, projected.Id);
        Assert.Equal(flow.Timestamp, projected.Timestamp);
        Assert.Equal("ei/first_contact", projected.Path);
        Assert.Equal("POST", projected.Method);
        Assert.Equal(200, projected.Status);
        Assert.Equal("FirstContactRequest", projected.RequestType);
        Assert.Equal("EggIncFirstContactResponse", projected.ResponseType);
    }

    [Fact]
    public void Build_CraftPayloadCarriesNoPlayerIdentifier() {
        var request = new CraftArtifactRequest {
            Rinfo = new BasicRequestInfo { EiUserId = Eid, Version = "1.37" },
            Spec = new ArtifactSpec {
                Name = ArtifactSpec.Types.Name.TungstenAnkh,
                Level = ArtifactSpec.Types.Level.Lesser,
                Rarity = ArtifactSpec.Types.Rarity.Common
            },
            GoldPricePaid = 3982,
            CraftingCount = 17
        };
        var response = new CraftArtifactResponse {
            ItemId = 991,
            RarityAchieved = ArtifactSpec.Types.Rarity.Rare
        };

        var draft = new ArtifactContributionKind().Build(
            ProtoFlow("ei_afx/craft_artifact", request, response));

        Assert.NotNull(draft);
        Assert.DoesNotContain(Eid, draft.PayloadJson, StringComparison.Ordinal);
        Assert.DoesNotContain("eiUserId", draft.PayloadJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("deviceId", draft.PayloadJson, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("1.37", draft.ClientVersion);

        var payload = JsonNode.Parse(draft.PayloadJson)!.AsObject();
        Assert.Equal("craft", payload["action"]!.GetValue<string>());
        Assert.Equal("TUNGSTEN_ANKH", payload["spec"]!["name"]!.GetValue<string>());
        Assert.Equal("RARE", payload["rarityAchieved"]!.GetValue<string>());
        Assert.Equal(17, payload["craftingCount"]!.GetValue<int>());
    }

    [Fact]
    public void Build_ConsumePayloadCarriesNoPlayerIdentifier() {
        var request = new ConsumeArtifactRequest {
            Rinfo = new BasicRequestInfo { EiUserId = Eid, Version = "1.37" },
            Spec = new ArtifactSpec {
                Name = ArtifactSpec.Types.Name.BookOfBasan,
                Level = ArtifactSpec.Types.Level.Greater,
                Rarity = ArtifactSpec.Types.Rarity.Legendary
            },
            Quantity = 3
        };
        var response = new ConsumeArtifactResponse {
            Success = true,
            OtherRewards = { new Reward { RewardType = RewardType.Gold, RewardAmount = 42 } }
        };

        var draft = new ArtifactContributionKind().Build(
            ProtoFlow("ei_afx/consume_artifact", request, response));

        Assert.NotNull(draft);
        Assert.DoesNotContain(Eid, draft.PayloadJson, StringComparison.Ordinal);

        var payload = JsonNode.Parse(draft.PayloadJson)!.AsObject();
        Assert.Equal("consume", payload["action"]!.GetValue<string>());
        Assert.Equal(3, payload["countRequested"]!.GetValue<int>());
        Assert.Equal(42, payload["goldenEggs"]!.GetValue<double>());
    }

    [Fact]
    public void Build_IgnoresRoutesThatAreNotArtifactActions() {
        var kind = new ArtifactContributionKind();
        Assert.Null(kind.Build(Flow("ei/first_contact")));
    }

    [Fact]
    public void Kinds_ExposeExactlyTheArtifactRoutes() {
        var kinds = new CaptureContributionKinds([new ArtifactContributionKind()]);

        Assert.Equal("artifact-observation", Assert.Single(kinds.KindNames));
        Assert.Equal(3, kinds.AllRoutes.Count);
        Assert.Contains("ei_afx/craft_artifact", kinds.AllRoutes);
        Assert.Contains("ei_afx/consume_artifact", kinds.AllRoutes);
        Assert.Contains("ei_afx/demote_artifact", kinds.AllRoutes);
        Assert.NotNull(kinds.For("ei_afx/demote_artifact"));
        Assert.Null(kinds.For("ei/get_periodicals"));
    }

    [Fact]
    public void DedupeHash_DiffersForRepeatedIdenticalOutcomes() {
        var kind = new ArtifactContributionKind();
        var request = new ConsumeArtifactRequest {
            Spec = new ArtifactSpec { Name = ArtifactSpec.Types.Name.LunarTotem },
            Quantity = 1
        };
        var response = new ConsumeArtifactResponse { Success = true };

        var first = kind.Build(ProtoFlow("ei_afx/consume_artifact", request, response) with { Id = 1 });
        var second = kind.Build(ProtoFlow("ei_afx/consume_artifact", request, response) with { Id = 2 });

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotEqual(first.DedupeHash, second.DedupeHash);
    }

    [Fact]
    public void Status_KnowsOnlyTheFourStates() {
        Assert.True(ContributedCaptureStatus.IsKnown(ContributedCaptureStatus.Recorded));
        Assert.True(ContributedCaptureStatus.IsKnown(ContributedCaptureStatus.Submitted));
        Assert.True(ContributedCaptureStatus.IsKnown(ContributedCaptureStatus.Approved));
        Assert.True(ContributedCaptureStatus.IsKnown(ContributedCaptureStatus.Rejected));
        Assert.False(ContributedCaptureStatus.IsKnown("published"));
        Assert.False(ContributedCaptureStatus.IsKnown(null));
    }

    private static DashboardFlow Flow(string path) =>
        new(7, "12:00:00", path, "POST", 200,
            "{\"rinfo\":{\"eiUserId\":\"" + Eid + "\"}}", "{\"secret\":true}", "AAEC", "BBBB",
            RequestType: "FirstContactRequest",
            ResponseType: "EggIncFirstContactResponse",
            RequestJsonRaw: "{\"rinfo\":{\"eiUserId\":\"" + Eid + "\"}}",
            ResponseJsonRaw: "{\"secret\":true}",
            Url: "https://auxbrain.com/" + path,
            ResponseText: "raw");

    private static DashboardFlow ProtoFlow(string path, IMessage request, IMessage response) =>
        new(7, "12:00:00", path, "POST", 200,
            null, null, "", null,
            RequestJsonRaw: JsonFormatter.Default.Format(request),
            ResponseJsonRaw: JsonFormatter.Default.Format(response));
}
