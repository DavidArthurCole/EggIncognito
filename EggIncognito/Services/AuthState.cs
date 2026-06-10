namespace EggIncognito.Services;

// Singleton flag: was Discord auth wired this run? Lets always-present services/controllers (the Auth
// controller, the mode endpoint) branch without depending on auth-only services.
public sealed record AuthState(bool Enabled);
