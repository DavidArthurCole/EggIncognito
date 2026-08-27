using EggIncognito.Services.Inspector;

namespace EggIncognito.Tests;

public class InspectorRefTests {
    [Fact]
    public void EndpointOnly_Parses() {
        var r = InspectorRefParser.Parse("#ep:ei/first_contact");
        Assert.Equal("ei/first_contact", r.EndpointPath);
        Assert.Null(r.ObjectName);
    }

    [Fact]
    public void ObjectOnly_Parses() {
        var r = InspectorRefParser.Parse("#obj:ContractsResponse");
        Assert.Null(r.EndpointPath);
        Assert.Equal("ContractsResponse", r.ObjectName);
    }

    [Fact]
    public void EndpointPlusObject_KeepsBoth() {
        var r = InspectorRefParser.Parse("#ep:ei/first_contact+obj:ContractsResponse");
        Assert.Equal("ei/first_contact", r.EndpointPath);
        Assert.Equal("ContractsResponse", r.ObjectName);
    }

    [Fact]
    public void EmptyHash_YieldsEmptyRef() {
        Assert.True(InspectorRefParser.Parse("").IsEmpty);
        Assert.True(InspectorRefParser.Parse("#").IsEmpty);
        Assert.True(InspectorRefParser.Parse(null).IsEmpty);
    }

    [Theory]
    [InlineData("#reference/ep:ei/first_contact", "ep:ei/first_contact")]
    [InlineData("#result/obj:ContractsResponse", "obj:ContractsResponse")]
    [InlineData("#reference/ep:ei_ctx/a/b/c", "ep:ei_ctx/a/b/c")]
    public void LegacyModePrefix_IsDroppedOnParse(string hash, string normalized) => Assert.Equal(normalized, InspectorRefParser.Format(InspectorRefParser.Parse(hash)));

    [Fact]
    public void UnknownKind_YieldsEmpty() {
        Assert.True(InspectorRefParser.Parse("#thing:whatever").IsEmpty);
        Assert.True(InspectorRefParser.Parse("#reference/thing:whatever").IsEmpty);
    }

    [Fact]
    public void RoutePathWithSeveralSlashes_SurvivesIntact() {
        var r = InspectorRefParser.Parse("#ep:ei_ctx/a/b/c");
        Assert.Equal("ei_ctx/a/b/c", r.EndpointPath);
    }

    [Fact]
    public void EmptyRef_FormatsToEmptyString() => Assert.Equal("", InspectorRefParser.Format(InspectorRefParser.Empty));

    [Theory]
    [InlineData("ep:ei/first_contact")]
    [InlineData("obj:ContractsResponse")]
    [InlineData("ep:ei/first_contact+obj:ContractsResponse")]
    [InlineData("ep:ei_ctx/a/b/c")]
    [InlineData("")]
    public void FormatOfParse_IsStable(string hash) {
        string once = InspectorRefParser.Format(InspectorRefParser.Parse(hash));
        string twice = InspectorRefParser.Format(InspectorRefParser.Parse(once));
        Assert.Equal(hash, once);
        Assert.Equal(once, twice);
    }

    [Theory]
    [InlineData("mock", InspectorTarget.Mock)]
    [InlineData("real", InspectorTarget.LiveViaServer)]
    [InlineData("custom", InspectorTarget.LiveViaProxy)]
    [InlineData("LiveViaServer", InspectorTarget.LiveViaServer)]
    [InlineData("LiveViaProxy", InspectorTarget.LiveViaProxy)]
    [InlineData("", InspectorTarget.Mock)]
    [InlineData("nonsense", InspectorTarget.Mock)]
    public void LegacyTargetStrings_MapOntoTheEnum(string stored, InspectorTarget expected) => Assert.Equal(expected, InspectorTargets.Parse(stored));
}
