namespace EggIncognito.Models.Periodicals;

public record EventsResponse(DateTimeOffset? CapturedAt, double? ServerTime, List<EventRow> Events);
