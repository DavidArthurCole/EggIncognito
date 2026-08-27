using System.Globalization;

namespace EggIncognito.Core;

public static class ByteFormat {
    public static string Humanize(long bytes, string decimals = "0.0", IFormatProvider? culture = null) {
        culture ??= CultureInfo.CurrentCulture;
        if (bytes < 1024) return bytes.ToString(culture) + " B";
        if (bytes < 1024 * 1024) return Kb(bytes, decimals, culture);
        return Mb(bytes, decimals, culture);
    }

    public static string Kb(long bytes, string decimals = "0.0", IFormatProvider? culture = null) => (bytes / 1024.0).ToString(decimals, culture ?? CultureInfo.CurrentCulture) + " KB";

    public static string Mb(long bytes, string decimals = "0.0", IFormatProvider? culture = null) => (bytes / (1024.0 * 1024.0)).ToString(decimals, culture ?? CultureInfo.CurrentCulture) + " MB";

    public static string KbOrMb(long bytes, string decimals = "0.0", IFormatProvider? culture = null) => bytes >= 1024 * 1024 ? Mb(bytes, decimals, culture) : Kb(bytes, decimals, culture);

    public static string Compact(long bytes) {
        if (bytes < 1024) return $"{bytes}B";
        var kb = bytes / 1024;
        return kb >= 1000 ? $"{kb / 1000}MB" : $"{kb}KB";
    }
}
