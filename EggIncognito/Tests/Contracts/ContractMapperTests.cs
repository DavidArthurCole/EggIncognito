using EggIncognito.Models.Contracts;
using EggIncognito.Services.Contracts;
using EggIncognito.Services.Events;
using Ei;
using Google.Protobuf;

namespace EggIncognito.Tests.Contracts;

public class ContractMapperTests {
    private static Contract BasicContract(string id = "c1") => new() {
        Identifier = id,
        Name = "Test Contract",
        StartTime = 1700000000,
        ExpirationTime = 1700100000,
        LengthSeconds = 100000
    };

    private static PeriodicalsResponse Response(params Contract[] contracts) {
        var response = new PeriodicalsResponse { Contracts = new ContractsResponse() };
        response.Contracts.Contracts.AddRange(contracts);
        return response;
    }

    [Fact]
    public void FromProto_ProphecyEggCount_UsesTopGradeSpec() {
        var c = BasicContract();
        c.GradeSpecs.Add(new Contract.Types.GradeSpec {
            Grade = Contract.Types.PlayerGrade.GradeC,
            Goals = { new Contract.Types.Goal { RewardType = RewardType.EggsOfProphecy, RewardAmount = 5 } }
        });
        c.GradeSpecs.Add(new Contract.Types.GradeSpec {
            Grade = Contract.Types.PlayerGrade.GradeAaa,
            Goals = { new Contract.Types.Goal { RewardType = RewardType.EggsOfProphecy, RewardAmount = 12 } }
        });
        var obs = ContractMapper.FromProto(c, ContractSources.Device, DateTimeOffset.UtcNow);
        Assert.Equal(12, obs!.ProphecyEggs);
    }

    [Fact]
    public void FromProto_ProphecyEggCount_FallsBackToLegacyGoals() {
        var c = BasicContract();
        c.Goals.Add(new Contract.Types.Goal { RewardType = RewardType.EggsOfProphecy, RewardAmount = 7 });
        c.Goals.Add(new Contract.Types.Goal { RewardType = RewardType.Gold, RewardAmount = 999 });
        var obs = ContractMapper.FromProto(c, ContractSources.Device, DateTimeOffset.UtcNow);
        Assert.Equal(7, obs!.ProphecyEggs);
    }

    [Fact]
    public void FromProto_StartFallback_WhenStartTimeMissing() {
        var c = BasicContract();
        c.StartTime = 0;
        c.ExpirationTime = 1700100000;
        c.LengthSeconds = 100000;
        var obs = ContractMapper.FromProto(c, ContractSources.Device, DateTimeOffset.UtcNow);
        Assert.Equal(UnixSeconds.ToTime(1700000000), obs!.Start);
        Assert.Equal(UnixSeconds.ToTime(1700100000), obs.End);
    }

    [Fact]
    public void FromProto_UltraFlag_FromCcOnly() {
        var c = BasicContract();
        c.CcOnly = true;
        var obs = ContractMapper.FromProto(c, ContractSources.Device, DateTimeOffset.UtcNow);
        Assert.True(obs!.UltraOnly);
    }

    [Fact]
    public void FromPeriodicals_SkipsDebugAndTutorialContract() {
        var debug = BasicContract("debug-1");
        debug.Debug = true;
        var tutorial = BasicContract("first-contract");
        var normal = BasicContract("normal-1");
        var observations = ContractMapper.FromPeriodicals(Response(debug, tutorial, normal), DateTimeOffset.UtcNow);
        var obs = Assert.Single(observations);
        Assert.Equal("normal-1", obs.ContractId);
    }

    [Fact]
    public void FromCarpet_SkipsMalformedBase64_KeepsValidSiblings() {
        var valid = BasicContract("valid-1");
        var rows = new List<CarpetContract> {
            new("valid-1", Convert.ToBase64String(valid.ToByteArray())),
            new("bad-1", "not-valid-base64!!!")
        };
        var observations = ContractMapper.FromCarpet(rows);
        var obs = Assert.Single(observations);
        Assert.Equal("valid-1", obs.ContractId);
        Assert.Equal(ContractSources.Carpet, obs.Source);
        Assert.Equal(obs.Start, obs.SeenAt);
    }
}
