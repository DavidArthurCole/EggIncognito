namespace EggIncognito.Services;

// Singleton flags for the SyncKit-only login path, so always-present services can branch without
// depending on auth-only services. Widget login (AuthController.RedeemCode) mints its own cookie via
// the Identity API; it needs the API configured plus the host serving /synckit-login.js.
public sealed record AuthState(bool IdentityApiEnabled, string? IdentityHostUrl = null)
{
    public bool WidgetEnabled => IdentityApiEnabled && !string.IsNullOrWhiteSpace(IdentityHostUrl);
    public bool Enabled => WidgetEnabled;
}
