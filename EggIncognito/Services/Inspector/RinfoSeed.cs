namespace EggIncognito.Services.Inspector;

public sealed class RinfoSeed {
    public string EiUserId { get; set; } = "";
    public string ClientVersion { get; set; } = "";
    public string Version { get; set; } = "";
    public string Build { get; set; } = "";
    public string Platform { get; set; } = "";
    public string Country { get; set; } = "";
    public string Language { get; set; } = "";
    public bool Debug { get; set; }

    public RinfoSeed OverlaidWith(RinfoSeed? over) {
        if (over is null) return this;
        return new RinfoSeed {
            EiUserId = Pick(EiUserId, over.EiUserId),
            ClientVersion = Pick(ClientVersion, over.ClientVersion),
            Version = Pick(Version, over.Version),
            Build = Pick(Build, over.Build),
            Platform = Pick(Platform, over.Platform),
            Country = Pick(Country, over.Country),
            Language = Pick(Language, over.Language),
            Debug = over.Debug || Debug
        };
    }

    private static string Pick(string under, string over) =>
        string.IsNullOrWhiteSpace(over) ? under : over;
}
