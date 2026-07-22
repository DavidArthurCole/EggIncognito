using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Tests.Devices;

public class DeviceFileParserTests {
    [Fact]
    public void IsDeviceFile_MatchesPattern() {
        Assert.True(DeviceFileParser.IsDeviceFile("android.egidevice.0"));
        Assert.True(DeviceFileParser.IsDeviceFile("ios.egidevice.1"));
        Assert.False(DeviceFileParser.IsDeviceFile("runner-android.env"));
        Assert.False(DeviceFileParser.IsDeviceFile("runner-ios.env"));
        Assert.False(DeviceFileParser.IsDeviceFile("readme.md"));
    }

    [Fact]
    public void Parse_ReadsAllKeys() {
        var p = DeviceFileParser.Parse("android.egidevice.0",
            "Id=frame-android\nPlatform=android\nLabel=Samsung Galaxy A15\nTarget=RF8X20GLYDY\nPackage=com.x");
        Assert.NotNull(p);
        Assert.Equal(0, p!.Order);
        Assert.Equal("frame-android", p.Id);
        Assert.Equal("android", p.Platform);
        Assert.Equal("Samsung Galaxy A15", p.Label);
        Assert.Equal("RF8X20GLYDY", p.Target);
        Assert.Equal("com.x", p.Package);
    }

    [Fact]
    public void Parse_PlatformFallsBackToFilenamePrefix() {
        var p = DeviceFileParser.Parse("ios.egidevice.1", "Id=frame-iphone\nTarget=udid");
        Assert.Equal("ios", p!.Platform);
    }

    [Fact]
    public void Parse_BodyPlatformWinsOverFilename() {
        var p = DeviceFileParser.Parse("ios.egidevice.1", "Id=x\nPlatform=android\nTarget=t");
        Assert.Equal("android", p!.Platform);
    }

    [Fact]
    public void Parse_IgnoresCommentsAndBlankLines() {
        var p = DeviceFileParser.Parse("android.egidevice.0", "# a comment\n\nId=x\n  # indented\nTarget=t\n");
        Assert.Equal("x", p!.Id);
        Assert.Equal("t", p.Target);
    }

    [Fact]
    public void Parse_NonNumericIndexSortsLast() {
        var p = DeviceFileParser.Parse("android.egidevice.spare", "Id=x\nTarget=t");
        Assert.Equal(int.MaxValue, p!.Order);
    }

    [Fact]
    public void Parse_NonDeviceFileReturnsNull() => Assert.Null(DeviceFileParser.Parse("runner-android.env", "Id=x\nTarget=t"));

    [Fact]
    public void Parse_MissingKeysStayNull() {
        var p = DeviceFileParser.Parse("android.egidevice.0", "Platform=android");
        Assert.Null(p!.Id);
        Assert.Null(p.Target);
    }
}
