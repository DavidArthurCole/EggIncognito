namespace EggIncognito.Services;

// One source of endpoint response JSON, keyed by the file layout's path/eid scheme. Returns the raw
// Google.Protobuf-JSON bytes for the best match, or null. Implementations apply the same precedence
// (eid match beats global) and the path-param parent-walk fallback. Higher Priority wins when more
// than one source could answer (file = 0, DB overlay = 100).
public interface IEndpointSource
{
    byte[]? Lookup(string path, string? eid);
    int Priority { get; }
}
