using System.IO.Compression;
using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Tests.ProtoExtract;

public class SymbolizedBinaryStoreTests {
    [Fact]
    public void Get_ReturnsExactVersion_WhenPresent() {
        string dir = MakeDir(("a.ipa", "1.35.7", BigBinary()), ("b.ipa", "1.36.0", BigBinary()));
        try {
            var store = new SymbolizedBinaryStore(dir, b => b.Length >= 200);
            var r = store.Get("1.36.0");
            Assert.True(r.Ok, r.Diagnostics);
            Assert.True(r.ExactVersion);
            Assert.Equal("1.36.0", r.Version);
        } finally {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Get_FallsBackToNewest_WhenNoExact() {
        string dir = MakeDir(("a.ipa", "1.35.5", BigBinary()), ("b.ipa", "1.35.7", BigBinary()));
        try {
            var store = new SymbolizedBinaryStore(dir, b => b.Length >= 200);
            var r = store.Get("1.36.0");
            Assert.True(r.Ok);
            Assert.False(r.ExactVersion);
            Assert.Equal("1.35.7", r.Version);
        } finally {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Get_Fails_WhenNoSymbolizedIpa() {
        string dir = MakeDir(("small.ipa", "1.36.0", new byte[100]));
        try {
            var store = new SymbolizedBinaryStore(dir, b => b.Length >= 200);
            var r = store.Get("1.36.0");
            Assert.False(r.Ok);
            Assert.Contains("no symbolized", r.Diagnostics, StringComparison.OrdinalIgnoreCase);
        } finally {
            Directory.Delete(dir, true);
        }
    }

    private static byte[] BigBinary() => new byte[200];

    private static string MakeDir(params (string name, string version, byte[] exec)[] ipas) {
        string dir = Path.Combine(Path.GetTempPath(), "symstore-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        foreach ((string name, string version, byte[] exec) in ipas) {
            using var fs = File.Create(Path.Combine(dir, name));
            using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
            var plist = zip.CreateEntry("Payload/Egg.app/Info.plist");
            using (var w = new StreamWriter(plist.Open())) {
                w.Write($"<plist><dict><key>CFBundleShortVersionString</key><string>{version}</string>" +
                        $"<key>CFBundleExecutable</key><string>egginc</string></dict></plist>");
            }

            var ent = zip.CreateEntry("Payload/Egg.app/egginc");
            using var es = ent.Open();
            es.Write(exec, 0, exec.Length);
        }

        return dir;
    }
}
