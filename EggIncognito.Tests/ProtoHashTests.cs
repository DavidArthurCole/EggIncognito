using System.Security.Cryptography;
using EggIncognito.Core;

namespace EggIncognito.Tests;

public class ProtoHashTests {
    [Fact]
    public void Hashes_A_Known_File_Deterministically() {
        string dir = Directory.CreateTempSubdirectory().FullName;
        string protoDir = Path.Combine(dir, "EggIncognito.Core", "Proto");
        Directory.CreateDirectory(protoDir);
        File.WriteAllText(Path.Combine(protoDir, "ei.proto"), "syntax = \"proto2\";\n");

        string got = ProtoHash.Current(dir);

        string want = Convert.ToHexString(
            SHA256.HashData(File.ReadAllBytes(Path.Combine(protoDir, "ei.proto")))).ToLowerInvariant();
        Assert.Equal(want, got);
    }
}
