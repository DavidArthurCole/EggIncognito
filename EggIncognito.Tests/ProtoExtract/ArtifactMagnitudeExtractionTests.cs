using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Tests.ProtoExtract;

public class ArtifactMagnitudeExtractionTests {
    [Fact]
    public void Init13_is_not_a_valid_artifact_source_composed_not_stored() {
        if (!BinaryFixture.TryLoad(out var bin)) return;

        var composed = StaticInitDoubleExtractor.Extract(bin, "___cxx_global_var_init.13", 200000);
        Assert.True(composed.Ok, composed.Diagnostics);
        Assert.True(StaticInitDoubleExtractorTests.Has(composed.Values, 1.16),
            "init.13 materializes 1.16 arithmetically");

        var pat = System.BitConverter.GetBytes(1.16);
        var storedAsF64 = false;
        for (var i = 0; i <= bin.Length - 8 && !storedAsF64; i++) {
            var m = true;
            for (var k = 0; k < 8; k++)
                if (bin[i + k] != pat[k]) { m = false; break; }
            storedAsF64 = m;
        }
        Assert.False(storedAsF64,
            "1.16 exists nowhere as a stored f64: init.13's doubles are composed arithmetic, NOT the artifact data table");
    }

    [Fact]
    public void Real_multiplier_constants_are_present_in_const_but_unattributed() {
        if (!BinaryFixture.TryLoad(out var bin)) return;

        var r = ArtifactMultiplierExtractor.Locate(bin);
        Assert.NotEmpty(r.Located);
        Assert.All(r.Located, h => Assert.EndsWith(",__const", h.Section));
        Assert.False(r.AllAttributed,
            "locator reports candidate constants only; per-artifact attribution is not yet done");
    }

    [Fact]
    public void Only_116_multiplier_is_absent_as_f64_documented_gap() {
        if (!BinaryFixture.TryLoad(out var bin)) return;

        var r = ArtifactMultiplierExtractor.Locate(bin);
        Assert.Equal([1.16], r.MissingAsF64);
    }
}
