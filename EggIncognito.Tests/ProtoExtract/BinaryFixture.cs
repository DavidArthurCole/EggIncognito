using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Tests.ProtoExtract;

public static class BinaryFixture {
    public static bool TryLoad(out byte[] bin) {
        foreach (var rel in new[] { "../../../../captures/egginc", "../../../../../captures/egginc", "../../../../EggIncognito/captures/egginc" }) {
            var full = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, rel));
            if (File.Exists(full)) {
                bin = File.ReadAllBytes(full);
                return true;
            }
        }

        foreach (var rel in new[] { "../../../../captures/ipas", "../../../../../captures/ipas" }) {
            var full = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, rel));
            if (!Directory.Exists(full)) continue;
            var store = new SymbolizedBinaryStore(full);
            var r = store.Get(null);
            if (r.Ok && r.Bytes is not null) {
                bin = r.Bytes;
                return true;
            }
        }

        bin = [];
        return false;
    }
}
