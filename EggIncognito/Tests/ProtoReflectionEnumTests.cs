using EggIncognito.Core.Services;

namespace EggIncognito.Tests;

public class ProtoReflectionEnumTests {
    [Fact]
    public void AllMessageTypeNames_IsNonEmpty_SortedAndDistinct() {
        var names = new ProtoReflection().AllMessageTypeNames();
        Assert.NotEmpty(names);
        Assert.Equal(names.Count, names.Distinct().Count());
        var sorted = names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        Assert.Equal(sorted, names);
    }

    [Fact]
    public void AllMessageTypeNames_IncludesKnownTypes() {
        var names = new ProtoReflection().AllMessageTypeNames();

        Assert.Contains("Contract", names);
        Assert.Contains("EggIncFirstContactResponse", names);
    }

    [Fact]
    public void AllMessageTypeNames_AreResolvable() {
        var refl = new ProtoReflection();

        foreach (string name in refl.AllMessageTypeNames().Take(25))
            Assert.NotNull(refl.Schema(name));
    }
}
