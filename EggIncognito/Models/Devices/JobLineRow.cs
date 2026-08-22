namespace EggIncognito.Models.Devices;

public sealed record JobLineRow(
    DateTimeOffset At,
    string Level,
    string Text,
    string? Entry,
    long? Bytes,
    string? Sha256);
