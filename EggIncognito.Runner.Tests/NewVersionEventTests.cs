using System.Text.Json;
using EggIncognito.Core.Models;
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
        Assert.Contains("\"clientVersion\":null", json);
    }
}
