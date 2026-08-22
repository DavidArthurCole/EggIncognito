namespace EggIncognito.Models.Inspector;

public sealed record RinfoSeedResponse(
    string EiUserId,
    string ClientVersion,
    string Version,
    string Build,
    string Platform,
    string Country,
    string Language,
    bool Debug);
