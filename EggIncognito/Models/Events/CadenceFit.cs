namespace EggIncognito.Models.Events;

public sealed record CadenceFit(
    double SlopeSeconds,
    double InterceptSeconds,
    double NextEstimate,
    int Samples,
    double ResidualMadSeconds,
    double Goodness);
