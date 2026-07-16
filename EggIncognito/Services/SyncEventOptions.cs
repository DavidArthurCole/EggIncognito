namespace EggIncognito.Services;


public sealed class SyncEventOptions
{
    public string EventSecret { get; init; } = "";
    public string ApkFetchRoot { get; init; } = "";
}
