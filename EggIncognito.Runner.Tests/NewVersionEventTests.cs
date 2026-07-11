using System.Text.Json;
using SyncKit.Contract;
using Xunit;

namespace EggIncognito.Runner.Tests;

public class NewVersionEventTests
{
    [Fact]
    public void Serializes_FrozenJsonPropertyNames()
    {
        var evt = new NewVersionEvent
        {
            Package = "com.auxbrain.egginc", AppVersion = "1.35.7", Build = "111343",
            ProtoSha = "abc", Platform = "android", DetectedAt = "2026-06-14T00:00:00Z",
        };
        var json = JsonSerializer.Serialize(evt);
        Assert.Contains("\"appVersion\":\"1.35.7\"", json);
        Assert.Contains("\"build\":\"111343\"", json);
        Assert.Contains("\"protoSha\":\"abc\"", json);
        Assert.Contains("\"platform\":\"android\"", json);
        Assert.DoesNotContain("clientVersion", json);
    }

    [Fact]
    public void Serializes_OmitsNullOptionalFields()
    {
        // AppVersion/Build/ClientVersion/Platform/ProtoTextB64 are [JsonIgnore(WhenWritingDefault)]
        // on SyncKit.Contract.NewVersionEvent, unlike the old local DTO which always wrote them.
        var evt = new NewVersionEvent { Package = "com.auxbrain.egginc", Version = "1.34", ProtoSha = "abc" };
        var json = JsonSerializer.Serialize(evt);
        Assert.DoesNotContain("appVersion", json);
        Assert.DoesNotContain("build", json);
        Assert.DoesNotContain("clientVersion", json);
        Assert.DoesNotContain("platform", json);
        Assert.DoesNotContain("protoTextB64", json);
    }
}
