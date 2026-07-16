using EggIncognito.Services;

namespace EggIncognito.Tests;

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
       
        Assert.Contains("Contract", names);
        Assert.Contains("EggIncFirstContactResponse", names);
    }

    [Fact]
    public void AllMessageTypeNames_AreResolvable()
    {
        var refl = new ProtoReflection();
       
       
        foreach (var name in refl.AllMessageTypeNames().Take(25))
            Assert.NotNull(refl.Schema(name));
    }
}
