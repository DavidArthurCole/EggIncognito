using Google.Protobuf;

namespace EggIncognito.Services;

// Maps an Ei.* message simple name (e.g. "PeriodicalsResponse") to its System.Type, by scanning the
// Ei assembly's generated message types once. The dynamic mock controller uses this to materialize a
// response message from a stored route's type name.
public static class ProtoTypeResolver
{
    private static readonly Lazy<IReadOnlyDictionary<string, System.Type>> ByName = new(() =>
    {
        var map = new Dictionary<string, System.Type>(StringComparer.Ordinal);
        var asm = typeof(Ei.AuthenticatedMessage).Assembly;
        foreach (var t in asm.GetTypes())
        {
            if (!typeof(IMessage).IsAssignableFrom(t) || t.IsAbstract) continue;
            // Only concrete generated messages have a public parameterless ctor.
            if (t.GetConstructor(System.Type.EmptyTypes) is null) continue;
            map[t.Name] = t;
        }
        return map;
    });

    public static System.Type? Resolve(string simpleName)
        => ByName.Value.TryGetValue(simpleName, out var t) ? t : null;

    public static IMessage NewInstance(System.Type t) => (IMessage)Activator.CreateInstance(t)!;
}
