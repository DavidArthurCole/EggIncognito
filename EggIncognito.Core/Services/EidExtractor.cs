namespace EggIncognito.Services;

// Pulls the EID (UserId) out of a request's base64 AuthenticatedMessage data param, or null if the
// data is absent / not a wrapped message / has no UserId. Shared by MockApiControllerBase and the
// DynamicMockController so the per-EID selection logic cannot diverge between them.
public static class EidExtractor
{
    public static string? FromData(string? data)
    {
        if (data is null) return null;
        try
        {
            var bytes = Convert.FromBase64String(data);
            var msg = Ei.AuthenticatedMessage.Parser.ParseFrom(bytes);
            return string.IsNullOrEmpty(msg.UserId) ? null : msg.UserId;
        }
        catch { return null; }
    }
}
