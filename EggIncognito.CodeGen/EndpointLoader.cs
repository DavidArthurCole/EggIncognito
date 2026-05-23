using System.Text.RegularExpressions;

namespace EggIncognito.CodeGen;

public record EndpointEntry(string Path, string RequestType, string ResponseType)
{
    public string Slug => Path.TrimEnd('/').Replace('/', '_');
}

public static class EndpointLoader
{
    private static readonly Regex EntryPattern = new(
        @"- path:\s+(\S+)\s+requestType:\s+(\S+)\s+responseType:\s+(\S+)",
        RegexOptions.Multiline);

    public static List<EndpointEntry> Load(string yamlPath)
    {
        var yaml = File.ReadAllText(yamlPath);
        return EntryPattern.Matches(yaml)
            .Select(m => new EndpointEntry(
                m.Groups[1].Value.TrimEnd('/'),
                m.Groups[2].Value,
                m.Groups[3].Value))
            .ToList();
    }

    public static string FindRepoRoot()
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
