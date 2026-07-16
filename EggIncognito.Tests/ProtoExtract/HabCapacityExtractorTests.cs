using EggIncognito.Services.ProtoExtract;
using Xunit;

namespace EggIncognito.Tests.ProtoExtract;

public class HabCapacityExtractorTests
{
    private static byte[]? Bin()
    {
        foreach (var rel in new[] { "../../../../captures/ipas", "../../../../../captures/ipas" })
        {
            var full = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, rel));
            if (!Directory.Exists(full)) continue;
            var store = new SymbolizedBinaryStore(full);
            foreach (var v in new[] { "1.35.6", "1.35.7", "1.35.5" })
            {
                var r = store.Get(v);
                if (r.Ok && r.Bytes is not null) return r.Bytes;
            }
        }
        return null;
    }

    [Fact]
    public void Extracts_full_hab_capacity_sequence_from_binary()
    {
        var bin = Bin();
        if (bin is null) return;

        var r = HabCapacityExtractor.Extract(bin);
        Assert.True(r.Ok, r.Diagnostics);
        Assert.Equal(
            [250L, 500, 1000, 2000, 5000, 10000, 20000, 50000, 100000, 200000, 500000,
             1_000_000, 2_000_000, 5_000_000, 10_000_000, 25_000_000, 50_000_000, 100_000_000, 600_000_000],
            r.Capacities);
    }
}
