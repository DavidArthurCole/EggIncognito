using System.Text;

namespace EggIncognito.CodeGen.Generators;

public sealed class CSharpGenerator : IServerGenerator
{
    public string Language => "C#";

    public void Generate(IReadOnlyList<EndpointEntry> endpoints, string fixturesPath, string outputDir, int port)
    {
        var repoRoot = EndpointLoader.FindRepoRoot();

        CopyDirectory(Path.Combine(repoRoot, "EggIncognito"), Path.Combine(outputDir, "EggIncognito"),
            skip: d => d.Name is "bin" or "obj");
        CopyDirectory(Path.Combine(repoRoot, "EggIncognito.Generator"), Path.Combine(outputDir, "EggIncognito.Generator"),
            skip: d => d.Name is "bin" or "obj");

        File.WriteAllText(Path.Combine(outputDir, "EggIncognito.slnx"), """
            <Solution>
              <Project Path="EggIncognito.Generator/EggIncognito.Generator.csproj" />
              <Project Path="EggIncognito/EggIncognito.csproj" />
            </Solution>
            """, new UTF8Encoding(false));

        GoGenerator.WriteReadme(outputDir, port,
            run: "dotnet run --project EggIncognito",
            prereqs: ".NET 10 SDK");
    }

    private static void CopyDirectory(string src, string dst, Func<DirectoryInfo, bool>? skip = null)
    {
        Directory.CreateDirectory(dst);
        foreach (var file in new DirectoryInfo(src).GetFiles())
            File.Copy(file.FullName, Path.Combine(dst, file.Name), overwrite: true);
        foreach (var dir in new DirectoryInfo(src).GetDirectories())
        {
            if (skip?.Invoke(dir) == true) continue;
            CopyDirectory(dir.FullName, Path.Combine(dst, dir.Name), skip);
        }
    }
}
