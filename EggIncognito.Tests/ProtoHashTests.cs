using System.Security.Cryptography;
using EggIncognito.Core;

namespace EggIncognito.Tests;

public class ProtoHashTests {
    [Fact]
    public void Hashes_A_Known_File_Deterministically() {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var protoDir = Path.Combine(dir, "EggIncognito.Core", "Proto");
        Directory.CreateDirectory(protoDir);
        File.WriteAllText(Path.Combine(protoDir, "ei.proto"), "syntax = \"proto2\";\n");

        var got = ProtoHash.Current(dir);

        var want = Convert.ToHexString(
            SHA256.HashData(File.ReadAllBytes(Path.Combine(protoDir, "ei.proto")))).ToLowerInvariant();
        Assert.Equal(want, got);
    }
}
