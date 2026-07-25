using System.Globalization;
using System.Reflection;

namespace EggIncognito.Services;

public sealed record BuildInfo(string Version, string Sha, string ShortSha, string BuildDate, string RepoUrl) {
    public string CommitUrl =>
        Sha == "unknown" ? RepoUrl : $"{RepoUrl}/commit/{Sha}";

    public static BuildInfo Parse(string informationalVersion, string repoUrl, string? buildDate = null) {
        int plus = informationalVersion.IndexOf('+');
        string version = plus >= 0 ? informationalVersion[..plus] : informationalVersion;
        string sha = plus >= 0 ? informationalVersion[(plus + 1)..] : "unknown";
        string shortSha = sha == "unknown" ? "unknown" : sha[..Math.Min(7, sha.Length)];
        return new BuildInfo(version, sha, shortSha, buildDate ?? "unknown", repoUrl);
    }


    public static BuildInfo FromAssembly(string repoUrl) {
        var asm = Assembly.GetEntryAssembly() ?? typeof(BuildInfo).Assembly;
        string iv = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";


        string buildDate;
        try {
            string path = Environment.ProcessPath ?? asm.Location;
            buildDate = string.IsNullOrEmpty(path)
                ? "unknown"
                : File.GetLastWriteTimeUtc(path).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        } catch {
            buildDate = "unknown";
        }

        return Parse(iv, repoUrl, buildDate);
    }
}
