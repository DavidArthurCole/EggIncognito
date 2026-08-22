namespace EggIncognito.Models.Periodicals;

public record EventRow(
    string Identifier,
    string Type,
    string Subtitle,
    double Multiplier,
    double StartTime,
    double Duration,
    double? EndTime,
    bool CcOnly,
    string? Icon);
