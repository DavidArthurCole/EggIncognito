using Bunit;
using EggIncognito.Components.Shared;

namespace EggIncognito.Tests;

// Smoke test for the bUnit toolchain plus the Icon port. Confirms a known icon renders its inner SVG
// and an unknown name renders an empty svg (mirrors the icons.js `?? ""`).
public class IconComponentTests : BunitContext
{
    [Fact]
    public void KnownIcon_RendersInnerSvgAndIconClass()
    {
        var cut = Render<Icon>(p => p.Add(c => c.Name, "gear"));
        var svg = cut.Find("svg");
        Assert.Contains("icon", svg.ClassList);
        Assert.NotEmpty(svg.Children); // gear has a <circle> + <path>
    }

    [Fact]
    public void Class_IsAppendedToIconClass()
    {
        var cut = Render<Icon>(p => p.Add(c => c.Name, "play").Add(c => c.Class, "spinning"));
        var svg = cut.Find("svg");
        Assert.Contains("icon", svg.ClassList);
        Assert.Contains("spinning", svg.ClassList);
    }

    [Fact]
    public void UnknownIcon_RendersEmptySvg()
    {
        var cut = Render<Icon>(p => p.Add(c => c.Name, "nope"));
        var svg = cut.Find("svg");
        Assert.Empty(svg.Children);
    }
}
