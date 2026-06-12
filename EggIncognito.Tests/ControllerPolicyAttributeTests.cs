using System.Reflection;
using EggIncognito.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EggIncognito.Tests;

// Cheap reflection guards for controller-level hardening attributes: the tools surface (decode +
// diagnose parse arbitrary client base64) sits behind the read limiter, and the HAR upload carries
// an explicit request-size cap.
public class ControllerPolicyAttributeTests
{
    [Fact]
    public void ToolsController_HasReadRateLimitPolicy()
    {
        var attr = typeof(ToolsController).GetCustomAttribute<EnableRateLimitingAttribute>();
        Assert.NotNull(attr);
        Assert.Equal("read", attr!.PolicyName);
    }

    [Fact]
    public void ImportHar_HasRequestSizeLimit()
    {
        var attr = typeof(ImportController).GetMethod("Har")!
            .GetCustomAttribute<RequestSizeLimitAttribute>();
        Assert.NotNull(attr);
    }
}
