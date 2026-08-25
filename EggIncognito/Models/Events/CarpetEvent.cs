namespace EggIncognito.Models.Events;

public sealed record CarpetEvent {
    public string? Id { get; init; }

    public string? Type { get; init; }

    public string? Message { get; init; }

    public double Multiplier { get; init; }

    public bool Ultra { get; init; }

    public double StartTimestamp { get; init; }

    public double EndTimestamp { get; init; }
}
