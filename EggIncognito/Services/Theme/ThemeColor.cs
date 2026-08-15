using System.Globalization;

namespace EggIncognito.Services.Theme;

public readonly record struct ThemeColor(double L, double C, double H, string? Hex) {
    public static ThemeColor? FromHex(string? hex) {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        string h = hex.Trim().TrimStart('#').ToLowerInvariant();
        if (h.Length == 3) h = $"{h[0]}{h[0]}{h[1]}{h[1]}{h[2]}{h[2]}";
        if (h.Length != 6) return null;
        foreach (char ch in h) {
            if (!Uri.IsHexDigit(ch)) return null;
        }

        int r = int.Parse(h[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        int g = int.Parse(h[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        int b = int.Parse(h[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var (l, a, bb) = LinearToOklab(SrgbToLinear(r / 255.0), SrgbToLinear(g / 255.0), SrgbToLinear(b / 255.0));
        double chroma = Math.Sqrt(a * a + bb * bb);
        double hue = Math.Atan2(bb, a) * 180.0 / Math.PI;
        if (hue < 0) hue += 360.0;
        return new ThemeColor(l, chroma, hue, "#" + h);
    }

    public static ThemeColor FromOklch(double l, double c, double h) {
        l = Math.Clamp(l, 0.0, 1.0);
        c = Math.Clamp(c, 0.0, 0.5);
        h %= 360.0;
        if (h < 0) h += 360.0;
        return new ThemeColor(l, c, h, null);
    }

    public string ToCss() {
        if (Hex is not null) return Hex;
        return string.Create(CultureInfo.InvariantCulture,
            $"oklch({Math.Round(L * 100.0, 1):0.#}% {Math.Round(C, 3):0.###} {Math.Round(H, 1):0.#})");
    }

    public ThemeColor RotateHue(double degrees) {
        double h = (H + degrees) % 360.0;
        if (h < 0) h += 360.0;
        return this with { H = h, Hex = null };
    }

    public (double L, double A, double B) ToOklab() {
        double rad = H * Math.PI / 180.0;
        return (L, C * Math.Cos(rad), C * Math.Sin(rad));
    }

    public (double R, double G, double B, bool Clipped) ToLinearSrgb() {
        var (l, a, b) = ToOklab();
        double l2 = l + 0.3963377774 * a + 0.2158037573 * b;
        double m2 = l - 0.1055613458 * a - 0.0638541728 * b;
        double s2 = l - 0.0894841775 * a - 1.2914855480 * b;
        double lc = l2 * l2 * l2;
        double mc = m2 * m2 * m2;
        double sc = s2 * s2 * s2;
        double r = 4.0767416621 * lc - 3.3077115913 * mc + 0.2309699292 * sc;
        double g = -1.2684380046 * lc + 2.6097574011 * mc - 0.3413193965 * sc;
        double bl = -0.0041960863 * lc - 0.7034186147 * mc + 1.7076147010 * sc;
        bool clipped = r is < 0 or > 1 || g is < 0 or > 1 || bl is < 0 or > 1;
        return (Math.Clamp(r, 0.0, 1.0), Math.Clamp(g, 0.0, 1.0), Math.Clamp(bl, 0.0, 1.0), clipped);
    }

    public double WcagLuminance() {
        var (r, g, b, _) = ToLinearSrgb();
        return 0.2126 * r + 0.7152 * g + 0.0722 * b;
    }

    public (double L, double A, double B) ToClippedOklab() {
        var (r, g, b, clipped) = ToLinearSrgb();
        return clipped ? LinearToOklab(r, g, b) : ToOklab();
    }

    public static double Contrast(ThemeColor a, ThemeColor b) {
        double la = a.WcagLuminance();
        double lb = b.WcagLuminance();
        double hi = Math.Max(la, lb);
        double lo = Math.Min(la, lb);
        return (hi + 0.05) / (lo + 0.05);
    }

    public static double DeltaE(ThemeColor a, ThemeColor b) {
        var (l1, a1, b1) = a.ToClippedOklab();
        var (l2, a2, b2) = b.ToClippedOklab();
        double d0 = l1 - l2;
        double d1 = a1 - a2;
        double d2 = b1 - b2;
        return Math.Sqrt(d0 * d0 + d1 * d1 + d2 * d2);
    }

    private static double SrgbToLinear(double c) =>
        c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);

    private static (double L, double A, double B) LinearToOklab(double r, double g, double b) {
        double l = 0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b;
        double m = 0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b;
        double s = 0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b;
        double lr = Math.Cbrt(l);
        double mr = Math.Cbrt(m);
        double sr = Math.Cbrt(s);
        return (
            0.2104542553 * lr + 0.7936177850 * mr - 0.0040720468 * sr,
            1.9779984951 * lr - 2.4285922050 * mr + 0.4505937099 * sr,
            0.0259040371 * lr + 0.7827717662 * mr - 0.8086757660 * sr);
    }
}
