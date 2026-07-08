using System.IO;

namespace EggIncognito.Tests;

public class AuthCallbackPathTests
{
    [Fact]
    public void CallbackPath_IsDiscordAuth()
    {
        var path = Path.Combine(RepoRoot(), "EggIncognito", "Services", "AuthSetup.cs");
        var text = File.ReadAllText(path);
        Assert.Contains("CallbackPath = \"/discord-auth\"", text);
        Assert.DoesNotContain("/signin-discord", text);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EggIncognito.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root (EggIncognito.slnx) not found");
    }
}
