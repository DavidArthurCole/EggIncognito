namespace EggIncognito.Services.Contributions;

public sealed record ContributionOptions(
    bool Enabled,
    int MaxRecordedPerUser,
    int MaxSubmittedPerUser,
    int BatchSize) {
    public static ContributionOptions Defaults() => new(true, 5000, 20000, 200);

    public static ContributionOptions Bind(IConfiguration config) {
        var d = Defaults();
        var s = config.GetSection("Contributions");
        return new ContributionOptions(
            s.GetValue("Enabled", d.Enabled),
            s.GetValue("MaxRecordedPerUser", d.MaxRecordedPerUser),
            s.GetValue("MaxSubmittedPerUser", d.MaxSubmittedPerUser),
            s.GetValue("BatchSize", d.BatchSize));
    }
}
