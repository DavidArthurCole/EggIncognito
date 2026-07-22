using System.Security.Cryptography;
using System.Text;
using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Tests;


public class ProtoCleanupParityTests {
    private const string ExpectedSha =
        "b0d689c5cdd1998da94f3a1967bd9549d3696cf674a88eab0001440cc2ea98b2";

    [Fact]
    public void Clean_RealFixture_MatchesPythonSha() {
        if (!TestFixtureFiles.TryRead("raw_ei.proto", out var eiBytes)) return;
        if (!TestFixtureFiles.TryRead("raw_common.proto", out var commonBytes)) return;

        var cleaned = ProtoCleanup.Clean(Encoding.UTF8.GetString(eiBytes), Encoding.UTF8.GetString(commonBytes));
        var sha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cleaned))).ToLowerInvariant();

        Assert.Equal(ExpectedSha, sha);
    }
}
