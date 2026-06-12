using EggIncognito.Capture;
using EggIncognito.Data.Services;

namespace EggIncognito.Tests;

// Token hashing + minting are pure statics. No DB round-trip here: the test project carries no EF
// test provider, matching the repo's tests-DB-free rule; the store body is thin EF upsert glue.
public class CaptureCredentialTests
{
    [Fact]
    public void Hash_IsDeterministicSha256Hex()
    {
        var h = CaptureCredentialStore.Hash("abc");
        Assert.Equal(h, CaptureCredentialStore.Hash("abc"));
        // SHA-256("abc"), the FIPS 180-2 test vector.
        Assert.Equal("BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD", h);
    }

    [Fact]
    public void Hash_DiffersForDifferentTokens()
    {
        Assert.NotEqual(CaptureCredentialStore.Hash("a"), CaptureCredentialStore.Hash("b"));
    }

    [Fact]
    public void Mint_Is48HexChars()
    {
        var t = CaptureCredentialStore.MintToken();
        Assert.Equal(48, t.Length); // 24 random bytes as hex
        Assert.Matches("^[0-9A-F]+$", t);
    }

    [Fact]
    public void Mint_TwoMintsDiffer()
    {
        Assert.NotEqual(CaptureCredentialStore.MintToken(), CaptureCredentialStore.MintToken());
    }

    // The front door hashes the presented password itself (Capture cannot reference Data); the two
    // implementations must never drift.
    [Fact]
    public void FrontDoorHash_MatchesStoreHash()
    {
        Assert.Equal(CaptureCredentialStore.Hash("some-token"), ProxyFrontDoor.Sha256Hex("some-token"));
    }
}
