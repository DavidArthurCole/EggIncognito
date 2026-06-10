using System.Text.Json;
using EggIncognito.Models;

namespace EggIncognito.Tests;

public class NewVersionEventDtoTests
{
    [Fact]
    public void Decodes_Frozen_Wire_Names()
    {
        var json = "{\"package\":\"com.auxbrain.egginc\",\"version\":\"1.34\",\"apkRef\":\"/x/base.apk\",\"protoSha\":\"deadbeef\",\"detectedAt\":\"2026-06-10T00:00:00Z\"}";
        var evt = JsonSerializer.Deserialize<NewVersionEvent>(json)!;
        Assert.Equal("com.auxbrain.egginc", evt.Package);
        Assert.Equal("1.34", evt.Version);
        Assert.Equal("/x/base.apk", evt.ApkRef);
        Assert.Equal("deadbeef", evt.ProtoSha);
        Assert.Equal("2026-06-10T00:00:00Z", evt.DetectedAt);
    }
}
