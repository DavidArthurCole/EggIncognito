namespace EggIncognito.Models.Events;

public sealed record GameEventListResponse(int Total, IReadOnlyList<GameEventDto> Events);
