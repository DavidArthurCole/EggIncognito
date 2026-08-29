using System.IO.Compression;
using EggIncognito.Core.Services.ProtoExtract;

namespace EggIncognito.Tests.ProtoExtract;

public sealed class SymbolizedIpaTests {
    [Fact]
    public void Read_ReturnsVersionAndExec_WhenValidIpa() {
        byte[] marker = "MARKERBYTES"u8.ToArray();
        byte[] ipa = BuildIpa("9.9.9", "Foo", "foo", marker);

        (string? version, byte[]? exec) = SymbolizedIpa.Read(ipa);

        Assert.Equal("9.9.9", version);
        Assert.Equal(marker, exec);
    }

    [Fact]
    public void Read_ReturnsNulls_WhenNotAZip() {
        byte[] garbage = [1, 2, 3, 4, 5];

        (string? version, byte[]? exec) = SymbolizedIpa.Read(garbage);

        Assert.Null(version);
        Assert.Null(exec);
    }

    [Fact]
    public void Read_ReturnsNulls_WhenInfoPlistMissing() {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true)) {
            var ent = zip.CreateEntry("Payload/Foo.app/foo");
            using var es = ent.Open();
            byte[] marker = "MARKERBYTES"u8.ToArray();
            es.Write(marker, 0, marker.Length);
        }

        (string? version, byte[]? exec) = SymbolizedIpa.Read(ms.ToArray());

        Assert.Null(version);
        Assert.Null(exec);
    }

    private static byte[] BuildIpa(string version, string appName, string execName, byte[] execBytes) {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true)) {
            var plist = zip.CreateEntry($"Payload/{appName}.app/Info.plist");
            using (var w = new StreamWriter(plist.Open())) {
                w.Write($"<plist><dict><key>CFBundleShortVersionString</key><string>{version}</string>" +
                        $"<key>CFBundleExecutable</key><string>{execName}</string></dict></plist>");
            }

            var ent = zip.CreateEntry($"Payload/{appName}.app/{execName}");
            using var es = ent.Open();
            es.Write(execBytes, 0, execBytes.Length);
        }

        return ms.ToArray();
    }
}
