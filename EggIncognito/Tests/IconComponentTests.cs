using Bunit;
using EggIdentity.Icons;
using EggIdentity.UI;
using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Tests;

public class IconComponentTests : BunitContext {
    [Fact]
    public void KnownIcon_RendersNestedSvgInsideIconSpan() {
        var cut = Render<Icon>(p => p.Add(c => c.Name, "settings"));
        var span = cut.Find("span");
        Assert.Contains("icon", span.ClassList);
        var svg = span.QuerySelector("svg");
        Assert.NotNull(svg);
        Assert.NotEmpty(svg!.Children);
    }

    [Fact]
    public void Class_IsAppendedToIconSpanClass() {
        var cut = Render<Icon>(p => p.Add(c => c.Name, "play").Add(c => c.Class, "spinning"));
        var span = cut.Find("span");
        Assert.Contains("icon", span.ClassList);
        Assert.Contains("spinning", span.ClassList);
        Assert.NotNull(span.QuerySelector("svg"));
    }

    [Fact]
    public void UnknownIcon_RendersEmptyIconSpanWithNoSvg() {
        var cut = Render<Icon>(p => p.Add(c => c.Name, "nope"));
        var span = cut.Find("span");
        Assert.Contains("icon", span.ClassList);
        Assert.Null(span.QuerySelector("svg"));
    }

    [Fact]
    public void PlatformIcons_ResolveToPackNames() {
        Assert.Contains(PlatformIcons.For("ios"), IconPack.Names);
        Assert.Contains(PlatformIcons.For("android"), IconPack.Names);
        Assert.Contains(PlatformIcons.For(null), IconPack.Names);
    }
}
