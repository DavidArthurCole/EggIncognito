using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Tests.Devices;

public class AdbHostKeyTests {
    private const string Literal = "QAAAliteral= literal@host";
    private const string FromPath = "QAAApath= path@host";
    private const string FromAndroidHome = "QAAAhome= home@host";
    private const string FromProfile = "QAAAprofile= profile@host";

    [Fact]
    public void Literal_BeatsEverything() {
        using var tmp = new TempDir();
        string path = tmp.Combine("configured.pub");
        File.WriteAllText(path, FromPath);
        var config = new VirtualDeviceConfig { AdbPublicKey = "  " + Literal + "\n", AdbPublicKeyPath = path };

        Assert.Equal(Literal, AdbHostKey.Resolve(config, tmp.Path, tmp.Path));
    }

    [Fact]
    public void ConfiguredPath_BeatsAndroidUserHome_AndProfile() {
        using var tmp = new TempDir();
        string path = tmp.Combine("configured.pub");
        File.WriteAllText(path, FromPath + "\n");
        string home = tmp.CreateSubdir();
        File.WriteAllText(Path.Combine(home, AdbHostKey.FileName), FromAndroidHome);
        string profile = ProfileWithKey(tmp);

        Assert.Equal(FromPath, AdbHostKey.Resolve(new VirtualDeviceConfig { AdbPublicKeyPath = path }, home, profile));
    }

    [Fact]
    public void AndroidUserHome_BeatsProfile() {
        using var tmp = new TempDir();
        string home = tmp.CreateSubdir();
        File.WriteAllText(Path.Combine(home, AdbHostKey.FileName), FromAndroidHome);
        string profile = ProfileWithKey(tmp);

        Assert.Equal(FromAndroidHome, AdbHostKey.Resolve(new VirtualDeviceConfig(), home, profile));
    }

    [Fact]
    public void Profile_IsUsedWhenNothingElseResolves() {
        using var tmp = new TempDir();
        string profile = ProfileWithKey(tmp);

        Assert.Equal(FromProfile, AdbHostKey.Resolve(new VirtualDeviceConfig(), null, profile));
    }

    [Fact]
    public void MissingOrEmptyFiles_AreSkipped() {
        using var tmp = new TempDir();
        string empty = tmp.Combine("empty.pub");
        File.WriteAllText(empty, " \n");

        var config = new VirtualDeviceConfig { AdbPublicKeyPath = empty };
        Assert.Null(AdbHostKey.Resolve(config, tmp.Combine("no-such-home"), tmp.CreateSubdir()));
    }

    [Fact]
    public void Candidates_EndWithRootHome() {
        var candidates = AdbHostKey.Candidates("/cfg/key.pub", "/home/u/.android", "/home/u").ToList();

        Assert.Equal("/cfg/key.pub", candidates[0]);
        Assert.Equal(Path.Combine("/home/u/.android", AdbHostKey.FileName), candidates[1]);
        Assert.Equal(Path.Combine("/home/u", ".android", AdbHostKey.FileName), candidates[2]);
        Assert.Equal(AdbHostKey.RootHomeKey, candidates[^1]);
    }

    private static string ProfileWithKey(TempDir tmp) {
        string profile = tmp.CreateSubdir();
        Directory.CreateDirectory(Path.Combine(profile, ".android"));
        File.WriteAllText(Path.Combine(profile, ".android", AdbHostKey.FileName), FromProfile);
        return profile;
    }
}
