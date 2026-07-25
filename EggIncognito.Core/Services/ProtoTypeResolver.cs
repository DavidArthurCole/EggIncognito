using Ei;
using Google.Protobuf;

namespace EggIncognito.Services;

public static class ProtoTypeResolver {
    private static readonly Lazy<IReadOnlyDictionary<string, Type>> ByName = new(() => {
        var map = new Dictionary<string, Type>(StringComparer.Ordinal);
        var asm = typeof(AuthenticatedMessage).Assembly;
        foreach (var t in asm.GetTypes()) {
            if (!typeof(IMessage).IsAssignableFrom(t) || t.IsAbstract) continue;

            if (t.GetConstructor(Type.EmptyTypes) is null) continue;
            map[t.Name] = t;
        }

        return map;
    });

    public static Type? Resolve(string simpleName)
        => ByName.Value.GetValueOrDefault(simpleName);

    public static IMessage NewInstance(Type t) => (IMessage)Activator.CreateInstance(t)!;
}
