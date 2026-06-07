namespace EggIncognito.Cli;

// Walks up from the running exe to the repo root (the dir containing a .slnx/.sln). Shared by the
// CaptureSession default paths and the CLI subcommands.
public static class RepoPaths
{
    public static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (dir.GetFiles("*.slnx").Length > 0 || dir.GetFiles("*.sln").Length > 0)
                return dir.FullName;
            dir = dir.Parent;
        }
        return Directory.GetCurrentDirectory();
    }
}
