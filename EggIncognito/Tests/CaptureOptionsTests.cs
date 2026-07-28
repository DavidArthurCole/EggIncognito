using EggIncognito.Capture;

namespace EggIncognito.Tests;

public class CaptureOptionsTests {
    [Fact]
    public void Parse_Defaults_WhenNoArgs() {
        var o = CaptureOptions.Parse([]);
        Assert.Equal(8080, o.Port);
        Assert.Equal(8090, o.DashboardPort);
        Assert.False(o.Overwrite);
        Assert.False(o.Verbose);
        Assert.False(o.NoDashboard);
        Assert.False(o.NoOpen);
        Assert.False(o.ForceOpen);
    }

    [Fact]
    public void Parse_ReadsValueAndFlagOptions() {
        var o = CaptureOptions.Parse(
        [
            "--port", "9000", "--dashboard-port", "9100", "--label", "my run",
            "--overwrite", "--no-dashboard", "--no-open", "--open"
        ]);
        Assert.Equal(9000, o.Port);
        Assert.Equal(9100, o.DashboardPort);
        Assert.Equal("my run", o.Label);
        Assert.True(o.Overwrite);
        Assert.True(o.NoDashboard);
        Assert.True(o.NoOpen);
        Assert.True(o.ForceOpen);
    }

    [Fact]
    public void Parse_VerboseAcceptsShortFlag() {
        Assert.True(CaptureOptions.Parse(["-v"]).Verbose);
        Assert.True(CaptureOptions.Parse(["--verbose"]).Verbose);
    }

    [Fact]
    public void HarFileName_PlainSession_WhenNoLabelOrEid() {
        var o = CaptureOptions.Parse([]);
        Assert.Equal("session.har", o.HarFileName());
    }

    [Fact]
    public void HarFileName_SanitizesLabel() {
        var o = CaptureOptions.Parse(["--label", "weird/name spaces!"]);
        Assert.Equal("session_weird_name_spaces_.har", o.HarFileName());
    }

    [Fact]
    public void HarFileName_AppendsValidEid() {
        var o = CaptureOptions.Parse(["--eid", "EI1234567890123456"]);
        Assert.Equal("session_EI1234567890123456.har", o.HarFileName());
    }

    [Fact]
    public void HarFileName_IgnoresMalformedEid() {
        var o = CaptureOptions.Parse(["--eid", "not-an-eid"]);
        Assert.Equal("session.har", o.HarFileName());
    }

    [Fact]
    public void UniquePath_FreeName_ReturnedAsIs() {
        string path = Path.Combine(Path.GetTempPath(), $"ei-unique-{Guid.NewGuid():N}.har");
        Assert.Equal(path, CaptureSession.UniquePath(path));
    }

    [Fact]
    public void UniquePath_TakenName_AppendsSuffix() {
        string dir = Path.Combine(Path.GetTempPath(), $"ei-unique-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "session.har");
        File.WriteAllText(path, "");
        Assert.Equal(Path.Combine(dir, "session_2.har"), CaptureSession.UniquePath(path));

        File.WriteAllText(Path.Combine(dir, "session_2.har"), "");
        Assert.Equal(Path.Combine(dir, "session_3.har"), CaptureSession.UniquePath(path));
    }
}
