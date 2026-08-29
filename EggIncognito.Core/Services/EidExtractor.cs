using Ei;

namespace EggIncognito.Core.Services;

public static class EidExtractor {
    public static string? FromData(string? data) {
        if (data is null) return null;
        try {
            byte[] bytes = Convert.FromBase64String(data);
            var msg = AuthenticatedMessage.Parser.ParseFrom(bytes);
            return string.IsNullOrEmpty(msg.UserId) ? null : msg.UserId;
        } catch {
            return null;
        }
    }
}
