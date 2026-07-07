namespace EggIncognito.Services;

// Singleton flags: which auth provider(s) wired this run. Lets always-present services and
// controllers branch without depending on auth-only services. Enabled is true when either wired;
// DiscordEnabled/AuthentikEnabled let a challenge endpoint 404 instead of throwing against a
// scheme its own provider never registered.
public sealed record AuthState(bool DiscordEnabled, bool AuthentikEnabled)
{
    public bool Enabled => DiscordEnabled || AuthentikEnabled;
}
