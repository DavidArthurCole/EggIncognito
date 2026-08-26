using EggIdentity.Contract;

namespace EggIncognito.Services.Auth;

public static class MockAccessGuard {
    public static readonly IReadOnlySet<string> AdminOnlyHosted =
        new HashSet<string>(StringComparer.Ordinal) { "ei_afx/zoom_zoom" };

    public static bool Blocks(string path, IAppMode mode, ICurrentUser user) =>
        AdminOnlyHosted.Contains(path) && mode.Mode == AppMode.Hosted && !user.IsAtLeast(UserRole.Admin);
}
