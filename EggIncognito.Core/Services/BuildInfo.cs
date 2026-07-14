using System.Reflection;

namespace EggIncognito.Services;

// Build identity parsed from the assembly InformationalVersion, "<version>+<sha>", stamped by the
// SourceRevisionId MSBuild target. Backs the Discord /verify command. Sha is "unknown" for a non-git
// build with no +suffix.
public sealed record BuildInfo(string Version, string Sha, string ShortSha, string BuildDate, string RepoUrl)
{
    public string CommitUrl =>
        Sha == "unknown" ? RepoUrl : $"{RepoUrl}/commit/{Sha}";

    public static BuildInfo Parse(string informationalVersion, string repoUrl, string? buildDate = null)
    {
        var plus = informationalVersion.IndexOf('+');
        var version = plus >= 0 ? informationalVersion[..plus] : informationalVersion;
        var sha = plus >= 0 ? informationalVersion[(plus + 1)..] : "unknown";
        var shortSha = sha == "unknown" ? "unknown" : sha[..Math.Min(7, sha.Length)];
        return new BuildInfo(version, sha, shortSha, buildDate ?? "unknown", repoUrl);
    }

    // Reads the entry or this assembly's InformationalVersion at runtime.
    public static BuildInfo FromAssembly(string repoUrl)
    {
        var asm = Assembly.GetEntryAssembly() ?? typeof(BuildInfo).Assembly;
        var iv = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
        // Build date: the on-disk entry file's last-write time, best-effort. Assembly.Location is empty
        // in a single-file publish, so use the process path (the exe) which single-file preserves.
        string buildDate;
        try
        {
            var path = Environment.ProcessPath ?? asm.Location;
            buildDate = string.IsNullOrEmpty(path)
                ? "unknown"
                : File.GetLastWriteTimeUtc(path).ToString("yyyy-MM-dd HH:mm 'UTC'");
        }
        catch { buildDate = "unknown"; }
        return Parse(iv, repoUrl, buildDate);
    }
}
