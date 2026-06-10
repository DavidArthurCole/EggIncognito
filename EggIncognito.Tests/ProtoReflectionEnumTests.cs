using EggIncognito.Services;

namespace EggIncognito.Tests;

// AllMessageTypeNames enumerates the compiled Ei.* messages (no DB, no network). Powers the Inspector
// Objects list + Documentation subjects.
public class ProtoReflectionEnumTests
{
    [Fact]
    public void AllMessageTypeNames_IsNonEmpty_SortedAndDistinct()
    {
        var names = new ProtoReflection().AllMessageTypeNames();
        Assert.NotEmpty(names);
        Assert.Equal(names.Count, names.Distinct().Count());
        var sorted = names.OrderBy(n => n, System.StringComparer.OrdinalIgnoreCase).ToList();
        Assert.Equal(sorted, names);
    }

    [Fact]
    public void AllMessageTypeNames_IncludesKnownTypes()
    {
        var names = new ProtoReflection().AllMessageTypeNames();
        // A couple of well-known upstream types must be present.
        Assert.Contains("Contract", names);
        Assert.Contains("EggIncFirstContactResponse", names);
    }

    [Fact]
    public void AllMessageTypeNames_AreResolvable()
    {
        var refl = new ProtoReflection();
        // Every enumerated name must resolve back to a schema (so the Objects list can't list a
        // type the schema endpoint then 404s on).
        foreach (var name in refl.AllMessageTypeNames().Take(25))
            Assert.NotNull(refl.Schema(name));
    }
}
