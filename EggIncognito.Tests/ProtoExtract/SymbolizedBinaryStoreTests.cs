using System.IO.Compression;
using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Tests.ProtoExtract;

public class SymbolizedBinaryStoreTests {
    [Fact]
    public void Get_ReturnsExactVersion_WhenPresent() {
        var dir = MakeDir(("a.ipa", "1.35.7", BigBinary()), ("b.ipa", "1.36.0", BigBinary()));
        try {
            var store = new SymbolizedBinaryStore(dir, isSymbolized: b => b.Length >= 200);
            var r = store.Get("1.36.0");
            Assert.True(r.Ok, r.Diagnostics);
            Assert.True(r.ExactVersion);
            Assert.Equal("1.36.0", r.Version);
        } finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Get_FallsBackToNewest_WhenNoExact() {
        var dir = MakeDir(("a.ipa", "1.35.5", BigBinary()), ("b.ipa", "1.35.7", BigBinary()));
        try {
            var store = new SymbolizedBinaryStore(dir, isSymbolized: b => b.Length >= 200);
            var r = store.Get("1.36.0");
            Assert.True(r.Ok);
            Assert.False(r.ExactVersion);
            Assert.Equal("1.35.7", r.Version);
        } finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Get_Fails_WhenNoSymbolizedIpa() {
        var dir = MakeDir(("small.ipa", "1.36.0", new byte[100]));
        try {
            var store = new SymbolizedBinaryStore(dir, isSymbolized: b => b.Length >= 200);
            var r = store.Get("1.36.0");
            Assert.False(r.Ok);
            Assert.Contains("no symbolized", r.Diagnostics, StringComparison.OrdinalIgnoreCase);
        } finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Get_RealIpa_ReturnsSymbolized_AndExtractsSiloConstants() {
        var dir = RealIpaDir();
        if (dir is null) return;

        var store = new SymbolizedBinaryStore(dir);
        var r = store.Get("1.35.7");
        if (!r.Ok || r.Bytes is null) return;
        Assert.True(MachoSymbols.Read(r.Bytes).Count > 400_000);

        var ex = FunctionConstantExtractor.Extract(r.Bytes, ["FarmScene10updateSilo"]);
        Assert.True(ex.Ok, ex.Diagnostics);
        Assert.Contains(ex.Floats, f => Math.Abs(f - 5.5) < 0.01);
    }

    private static string? RealIpaDir() {
        foreach (var rel in new[] { "../../../../captures/ipas", "../../../../../captures/ipas" }) {
            var full = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, rel));
            if (Directory.Exists(full) && Directory.EnumerateFiles(full, "*.ipa").Any()) return full;
        }
        return null;
    }

    private static byte[] BigBinary() => new byte[200];

    private static string MakeDir(params (string name, string version, byte[] exec)[] ipas) {
        var dir = Path.Combine(Path.GetTempPath(), "symstore-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        foreach (var (name, version, exec) in ipas) {
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
