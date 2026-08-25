namespace EggIncognito.Models.Events;

public sealed record EventStatsResponse(long Total, long Device, long Carpet, GameEventDto? Latest);
