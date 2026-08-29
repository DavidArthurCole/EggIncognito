using System.Text.RegularExpressions;

namespace EggIncognito.Core.Services;

public static partial class EidPattern {
    public const string DigitRun = @"EI\d{10,}";
    public const string Anchored = @"^EI\d{10,}$";

    public static Regex Contains => ContainsRegex();
    public static Regex Exact => ExactRegex();

    [GeneratedRegex(DigitRun)]
    private static partial Regex ContainsRegex();

    [GeneratedRegex(Anchored)]
    private static partial Regex ExactRegex();
}
