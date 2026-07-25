namespace EggIncognito.Services;

public interface IEndpointSource {
    int Priority { get; }
    byte[]? Lookup(string path, string? eid);
}
