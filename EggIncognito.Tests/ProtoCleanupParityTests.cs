using System.Security.Cryptography;
using System.Text;
using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Tests;

// Full-file parity against the real extracted ei.proto + common.proto (arm split, libegginc.so, the
// build proven live on frame 2026-06-14). The C# ProtoCleanup must produce byte-identical output to
// the python protocleanup.py, whose sha256 was captured from the live run. Fixtures live next to this
// test; resolved via the compile-time source path so no csproj copy wiring is needed.
public class ProtoCleanupParityTests
{
    private const string ExpectedSha =
        "b0d689c5cdd1998da94f3a1967bd9549d3696cf674a88eab0001440cc2ea98b2";

    [Fact]
    public void Clean_RealFixture_MatchesPythonSha()
    {
        var dir = Path.GetDirectoryName(SourcePath())!;
        var ei = File.ReadAllText(Path.Combine(dir, "raw_ei.proto"));
        var common = File.ReadAllText(Path.Combine(dir, "raw_common.proto"));

        var cleaned = ProtoCleanup.Clean(ei, common);
        var sha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cleaned))).ToLowerInvariant();

        Assert.Equal(ExpectedSha, sha);
    }

    static string SourcePath([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;
}
