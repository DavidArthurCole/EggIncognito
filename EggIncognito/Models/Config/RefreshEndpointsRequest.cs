namespace EggIncognito.Models.Config;

public sealed record RefreshEndpointsRequest(string? Salt, string? Platform);
