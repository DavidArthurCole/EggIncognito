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
            var slug = Path.GetFileNameWithoutExtension(jsonFile);
            if (!typeMap.TryGetValue(slug, out var descriptor))
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
}
