using System.IO;

namespace EggIncognito.Tests;

// Guards the chosen Discord OAuth callback path. A drift back to /signin-discord (or any other path)
// breaks the registered portal redirect URI, so pin it here. Reads the source rather than booting the
// auth middleware (which needs live Discord config).
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

    // Walk up from the test bin dir to the repo root (the dir holding EggIncognito.slnx).
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EggIncognito.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root (EggIncognito.slnx) not found");
    }
}
