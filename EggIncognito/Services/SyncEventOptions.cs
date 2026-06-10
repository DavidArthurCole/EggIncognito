namespace EggIncognito.Services;

// SyncEventOptions configures the inbound device-farm event endpoint. The endpoint is only active
// when EventSecret is set, matching the opt-in pattern of the bot (Discord:BotToken) and DB
// (ConnectionStrings:Postgres).
public sealed class SyncEventOptions
{
    public string EventSecret { get; init; } = "";
    public string ApkFetchRoot { get; init; } = "";
}
