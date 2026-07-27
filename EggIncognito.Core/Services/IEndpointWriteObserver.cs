namespace EggIncognito.Services;

public interface IEndpointWriteObserver {
    void OnEndpointWritten(string routePath, string json, string? previousJson = null);
}
