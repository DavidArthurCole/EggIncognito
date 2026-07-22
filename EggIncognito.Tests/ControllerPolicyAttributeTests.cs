using System.Reflection;
using EggIncognito.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EggIncognito.Tests;

public class ControllerPolicyAttributeTests {
    [Fact]
    public void ToolsController_HasReadRateLimitPolicy() {
        var attr = typeof(ToolsController).GetCustomAttribute<EnableRateLimitingAttribute>();
        Assert.NotNull(attr);
        Assert.Equal("read", attr!.PolicyName);
    }

    [Fact]
    public void ImportHar_HasRequestSizeLimit() {
        var attr = typeof(ImportController).GetMethod("Har")!
            .GetCustomAttribute<RequestSizeLimitAttribute>();
        Assert.NotNull(attr);
    }
}
