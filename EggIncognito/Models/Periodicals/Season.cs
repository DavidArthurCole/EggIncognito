namespace EggIncognito.Models.Periodicals;

public record Season(string Id, string Name, double StartTime, double NextStartTime, bool StartDerived, List<GradeGoalsRow> GradeGoals, List<SeasonEgg>? Colleggtibles);
