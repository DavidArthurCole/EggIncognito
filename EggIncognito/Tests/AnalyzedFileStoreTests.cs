using System.Text;
using EggIncognito.Data.Services;

namespace EggIncognito.Tests;

public class AnalyzedFileStoreTests {
    [Fact]
    public void Sha256Hex_matches_known_vectors() {
        Assert.Equal("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            AnalyzedFileStore.Sha256Hex([]));
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            AnalyzedFileStore.Sha256Hex(Encoding.ASCII.GetBytes("abc")));
    }

    [Fact]
    public void Sha256Hex_is_deterministic_and_lowercase() {
        byte[] bytes = [0x50, 0x4B, 0x03, 0x04, 0x11, 0x22];
        string a = AnalyzedFileStore.Sha256Hex(bytes);
        string b = AnalyzedFileStore.Sha256Hex(bytes);
        Assert.Equal(a, b);
        Assert.Equal(a.ToLowerInvariant(), a);
        Assert.Equal(64, a.Length);
    }
}
