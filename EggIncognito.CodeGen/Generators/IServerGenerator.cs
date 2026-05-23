namespace EggIncognito.CodeGen.Generators;

public interface IServerGenerator
{
    string Language { get; }
    void Generate(IReadOnlyList<EndpointEntry> endpoints, string fixturesPath, string outputDir, int port);
}
