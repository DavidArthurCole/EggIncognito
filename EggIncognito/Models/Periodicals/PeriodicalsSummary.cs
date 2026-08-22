namespace EggIncognito.Models.Periodicals;

public record PeriodicalsSummary(List<ExtractedRow> Extracted, ColleggtiblesInfo? Colleggtibles, ConfigInfo Config, List<FeedInfo> Feeds);
