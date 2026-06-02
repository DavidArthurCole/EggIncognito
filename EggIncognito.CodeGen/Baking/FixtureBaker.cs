using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace EggIncognito.CodeGen.Baking;

public static class FixtureBaker
{
    public static int Bake(string fixturesPath, Dictionary<string, MessageDescriptor> typeMap)
    {
        int count = 0, skipped = 0;
        foreach (var jsonFile in Directory.EnumerateFiles(fixturesPath, "*.json", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(fixturesPath, jsonFile).Replace('\\', '/');
            var endpointPath = ExtractEndpointPath(relative);
            if (endpointPath is null || !typeMap.TryGetValue(endpointPath, out var descriptor))
            {
                skipped++;
                continue;
            }

            try
            {
                var json = File.ReadAllText(jsonFile);
                var msg  = JsonParser.Default.Parse(json, descriptor);
                var binPath = Path.ChangeExtension(jsonFile, ".binpb");
                File.WriteAllBytes(binPath, msg.ToByteArray());
                count++;
                Console.WriteLine($"  OK  {Path.GetRelativePath(fixturesPath, jsonFile)}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  ERR {Path.GetRelativePath(fixturesPath, jsonFile)}: {ex.Message}");
            }
        }
        if (skipped > 0)
            Console.WriteLine($"  (skipped {skipped} file(s) with no matching endpoint)");
        return count;
    }

    private static string? ExtractEndpointPath(string relative)
    {
        if (relative.StartsWith("default/"))
            return relative["default/".Length..].Replace(".json", "");
        if (relative.StartsWith("eids/"))
        {
            var rest = relative["eids/".Length..];
            var slash = rest.IndexOf('/');
            return slash >= 0 ? rest[(slash + 1)..].Replace(".json", "") : null;
        }
        return null;
    }
}
