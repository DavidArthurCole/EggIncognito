using EggIncognito.Services.Inspector;

namespace EggIncognito.Tests;

public class InspectorRefTests {
    [Fact]
    public void EndpointOnly_DefaultsToResultMode() {
        var r = InspectorRefParser.Parse("#ep:ei/first_contact");
        Assert.Equal("ei/first_contact", r.EndpointPath);
        Assert.Null(r.ObjectName);
        Assert.Equal(InspectorReaderMode.Result, r.Mode);
    }

    [Fact]
    public void ReferenceMode_LeadsAndSurvives() {
        var r = InspectorRefParser.Parse("#reference/ep:ei/first_contact");
        Assert.Equal("ei/first_contact", r.EndpointPath);
        Assert.Equal(InspectorReaderMode.Reference, r.Mode);
    }

    [Fact]
    public void ObjectOnly_ReadsAsReference() {
        var r = InspectorRefParser.Parse("#obj:ContractsResponse");
        Assert.Null(r.EndpointPath);
        Assert.Equal("ContractsResponse", r.ObjectName);
        Assert.Equal(InspectorReaderMode.Reference, r.Mode);
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

    [Fact]
    public void ModeIsDroppedWhenObjectIsPresent() {
        var r = InspectorRefParser.Parse("#result/obj:ContractsResponse");
        Assert.Equal(InspectorReaderMode.Reference, r.Mode);
        Assert.Equal("obj:ContractsResponse", InspectorRefParser.Format(r));
    }

    [Fact]
    public void UnknownMode_IsDropped() {
        var r = InspectorRefParser.Parse("#bogus/ep:ei/first_contact");
        Assert.Equal("ei/first_contact", r.EndpointPath);
        Assert.Equal(InspectorReaderMode.Result, r.Mode);
    }

    [Fact]
    public void UnknownKind_YieldsEmpty() {
        Assert.True(InspectorRefParser.Parse("#thing:whatever").IsEmpty);
        Assert.True(InspectorRefParser.Parse("#reference/thing:whatever").IsEmpty);
    }

    [Fact]
    public void RoutePathWithSeveralSlashes_SurvivesIntact() {
        var r = InspectorRefParser.Parse("#reference/ep:ei_ctx/a/b/c");
        Assert.Equal("ei_ctx/a/b/c", r.EndpointPath);
        Assert.Equal(InspectorReaderMode.Reference, r.Mode);
    }

    [Fact]
    public void ResultModeIsNeverWritten() {
        var r = new InspectorRef("ei/first_contact", null, InspectorReaderMode.Result);
        Assert.Equal("ep:ei/first_contact", InspectorRefParser.Format(r));
    }

    [Fact]
    public void EmptyRef_FormatsToEmptyString() {
        Assert.Equal("", InspectorRefParser.Format(InspectorRefParser.Empty));
    }

    [Theory]
    [InlineData("ep:ei/first_contact")]
    [InlineData("reference/ep:ei/first_contact")]
    [InlineData("obj:ContractsResponse")]
    [InlineData("ep:ei/first_contact+obj:ContractsResponse")]
    [InlineData("reference/ep:ei_ctx/a/b/c")]
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
    public void LegacyTargetStrings_MapOntoTheEnum(string stored, InspectorTarget expected) {
        Assert.Equal(expected, InspectorTargets.Parse(stored));
    }
}
