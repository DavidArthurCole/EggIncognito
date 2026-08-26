namespace EggIncognito.Services.Feed;

public static class WebhookMask {
    public static string Mask(string url) {
        string[] parts = url.Split('/', StringSplitOptions.RemoveEmptyEntries);
        int i = Array.IndexOf(parts, "webhooks");
        if (i >= 0 && i + 2 < parts.Length) {
            string webhookId = parts[i + 1];
            string token = parts[i + 2];
            string last4 = token.Length <= 4 ? token : token[^4..];
            return $"webhooks/{webhookId}/...{last4}";
        }

        string tail = url.Length <= 6 ? url : url[^6..];
        return $"...{tail}";
    }
}
