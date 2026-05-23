using System.Reflection;
using Google.Protobuf.Reflection;

namespace EggIncognito.CodeGen.Baking;

public static class EndpointTypeMap
{
    private static readonly Assembly EiAssembly = typeof(Ei.AuthenticatedMessage).Assembly;

    public static Dictionary<string, MessageDescriptor> Build(IReadOnlyList<EndpointEntry> endpoints)
    {
        var map = new Dictionary<string, MessageDescriptor>(StringComparer.OrdinalIgnoreCase);
        foreach (var ep in endpoints)
        {
            var descriptor = ResolveDescriptor(ep.ResponseType);
            if (descriptor is not null)
                map[ep.Slug] = descriptor;
            else
                Console.Error.WriteLine($"  WARN: no proto type for '{ep.ResponseType}' (slug: {ep.Slug})");
        }
        return map;
    }

    private static MessageDescriptor? ResolveDescriptor(string typeName)
    {
        var type = EiAssembly.GetType($"Ei.{typeName}");
        if (type is null) return null;
        var prop = type.GetProperty("Descriptor", BindingFlags.Public | BindingFlags.Static);
        return prop?.GetValue(null) as MessageDescriptor;
    }
}
