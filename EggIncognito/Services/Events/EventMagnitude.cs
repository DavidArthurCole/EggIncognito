using System.Globalization;
using EggIncognito.Models.Events;

namespace EggIncognito.Services.Events;

public static class EventMagnitude {
    public static string Scalar(GameEventDto e) => Lead(e.Message) ?? Describe(e);

    public static string Describe(GameEventDto e) {
        if (e.Multiplier <= 0) return "";
        if (e.Multiplier < 1) return Percent(1 - e.Multiplier) + " off";
        return e.Multiplier.ToString("0.###", CultureInfo.InvariantCulture) + "x";
    }

    private static string Percent(double fraction) =>
        (fraction * 100).ToString("0.##", CultureInfo.InvariantCulture) + "%";

    private static string? Lead(string? message) {
        if (string.IsNullOrWhiteSpace(message)) return null;
        var token = message.TrimStart().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (token is null || !char.IsAsciiDigit(token[0])) return null;
        return token.TrimEnd('!', ',', '.');
    }
}
