namespace EggIncognito.Services;

public sealed record AuthState(
    bool IdentityApiEnabled,
    string? IdentityHostUrl = null,
    string SessionCookieName = "eggidentity_session") {
    public bool WidgetEnabled => IdentityApiEnabled && !string.IsNullOrWhiteSpace(IdentityHostUrl);
    public bool Enabled => WidgetEnabled;
}
