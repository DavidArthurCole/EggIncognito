namespace EggIncognito.Services;

public interface IEndpointSource {
    byte[]? Lookup(string path, string? eid);
    int Priority { get; }
}
