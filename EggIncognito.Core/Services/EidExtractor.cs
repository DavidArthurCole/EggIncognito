namespace EggIncognito.Services;


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
