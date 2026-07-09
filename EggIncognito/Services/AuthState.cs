namespace EggIncognito.Services;

// Singleton flags: which auth provider(s) wired this run, so always-present services can branch
// without depending on auth-only services. DiscordEnabled/AuthentikEnabled let a challenge endpoint
// 404 instead of throwing against a scheme its own provider never registered.
public sealed record AuthState(bool DiscordEnabled, bool AuthentikEnabled, string? IdentityHostUrl = null)
{
    public bool Enabled => DiscordEnabled || AuthentikEnabled;

    // The embedded popup widget needs a live cookie scheme (Discord or Authentik already wired) plus
    // the identity host serving /synckit-login.js and /identity/redeem.
    public bool WidgetEnabled => Enabled && !string.IsNullOrWhiteSpace(IdentityHostUrl);
}
