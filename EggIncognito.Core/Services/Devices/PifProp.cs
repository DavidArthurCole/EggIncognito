using System.Globalization;
using System.Text;

namespace EggIncognito.Core.Services.Devices;

public static class PifProp {
    public const string FileName = "custom.pif.prop";

    private const string DateFormat = "yyyy-MM-dd";
    private const string ReleasedOnPrefix = "# Released On:";
    private const string ExpiryPrefix = "# Estimated Expiry:";
    private const string Unknown = "Unknown";

    public static string Render(PifProfile p) {
        var sb = new StringBuilder();
        sb.Append("# Build Fields\n");
        sb.Append("MANUFACTURER=").Append(p.Manufacturer).Append('\n');
        sb.Append("MODEL=").Append(p.Model).Append('\n');
        sb.Append("FINGERPRINT=").Append(p.Fingerprint).Append('\n');
        sb.Append("BRAND=").Append(p.Brand).Append('\n');
        sb.Append("PRODUCT=").Append(p.Product).Append('\n');
        sb.Append("DEVICE=").Append(p.Device).Append('\n');
        sb.Append("RELEASE=").Append(p.Release).Append('\n');
        sb.Append("ID=").Append(p.Id).Append('\n');
        sb.Append("INCREMENTAL=").Append(p.Incremental).Append('\n');
        sb.Append("TYPE=user\n");
        sb.Append("TAGS=release-keys\n");
        sb.Append("SECURITY_PATCH=").Append(p.SecurityPatch).Append('\n');
        sb.Append("DEVICE_INITIAL_SDK_INT=").Append(p.DeviceInitialSdkInt.ToString(CultureInfo.InvariantCulture)).Append('\n');
        sb.Append('\n');
        sb.Append("# System Properties\n");
        sb.Append("*.build.id=").Append(p.Id).Append('\n');
        sb.Append("*.security_patch=").Append(p.SecurityPatch).Append('\n');
        sb.Append("*api_level=").Append(p.DeviceInitialSdkInt.ToString(CultureInfo.InvariantCulture)).Append('\n');
        sb.Append('\n');
        sb.Append("# Advanced Settings\n");
        sb.Append("verboseLogs=0\n");
        sb.Append("spoofApps=0\n");
        sb.Append("spoofBuild=1\n");
        sb.Append("spoofProps=1\n");
        sb.Append("spoofProvider=0\n");
        sb.Append("spoofSignature=0\n");
        sb.Append("spoofVendingFinger=0\n");
        sb.Append("spoofVendingSdk=0\n");
        sb.Append("spoofPixel=0\n");
        sb.Append('\n');
        sb.Append(ReleasedOnPrefix).Append(' ').Append(FormatDate(p.ReleasedOn)).Append('\n');
        sb.Append(ExpiryPrefix).Append(' ').Append(FormatDate(p.Expiry)).Append('\n');
        return sb.ToString();
    }

    public static PifProfile? Parse(string text) {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        DateOnly? releasedOn = null;
        DateOnly? expiry = null;
        foreach (string raw in text.Split('\n')) {
            string line = raw.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith('#')) {
                if (line.StartsWith(ReleasedOnPrefix, StringComparison.Ordinal)) releasedOn = ParseDate(line[ReleasedOnPrefix.Length..]);
                else if (line.StartsWith(ExpiryPrefix, StringComparison.Ordinal)) expiry = ParseDate(line[ExpiryPrefix.Length..]);
                continue;
            }

            int eq = line.IndexOf('=', StringComparison.Ordinal);
            if (eq <= 0) continue;
            values[line[..eq].Trim()] = line[(eq + 1)..].Trim();
        }

        if (!values.TryGetValue("MODEL", out string? model)
            || !values.TryGetValue("PRODUCT", out string? product)
            || !values.TryGetValue("DEVICE", out string? device)
            || !values.TryGetValue("ID", out string? id)
            || !values.TryGetValue("INCREMENTAL", out string? incremental)
            || !values.TryGetValue("SECURITY_PATCH", out string? securityPatch)) {
            return null;
        }

        int sdk = values.TryGetValue("DEVICE_INITIAL_SDK_INT", out string? sdkText)
                  && int.TryParse(sdkText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : PifProfile.LegacyInitialSdkInt;

        return new PifProfile(
            values.GetValueOrDefault("MANUFACTURER", "Google"),
            model,
            values.GetValueOrDefault("BRAND", "google"),
            product,
            device,
            values.GetValueOrDefault("RELEASE", "CANARY"),
            id,
            incremental,
            securityPatch,
            sdk,
            releasedOn,
            expiry);
    }

    private static string FormatDate(DateOnly? date) =>
        date is { } d ? d.ToString(DateFormat, CultureInfo.InvariantCulture) : Unknown;

    private static DateOnly? ParseDate(string text) =>
        DateOnly.TryParseExact(text.Trim(), DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
}
