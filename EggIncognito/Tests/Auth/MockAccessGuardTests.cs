using EggIdentity.Contract;
using EggIncognito.Services;
using EggIncognito.Services.Auth;

namespace EggIncognito.Tests.Auth;

public class MockAccessGuardTests {
    private sealed class FakeMode(AppMode mode) : IAppMode {
        public AppMode Mode => mode;
        public bool CanCapture => false;
        public bool CanWrite => false;
    }

    private sealed class FakeUser(UserRole role) : ICurrentUser {
        public bool IsAuthenticated => role != UserRole.Viewer;
        public Guid? UserId => null;
        public string? DiscordId => null;
        public string? Username => null;
        public string? Avatar => null;
        public string? AvatarUrl => null;
        public UserRole Role => role;
        public bool IsSupporter => false;
        public bool IsAtLeast(UserRole need) => need switch {
            UserRole.Admin => role == UserRole.Admin,
            _ => true,
        };
    }

    [Fact]
    public void HostedNonAdmin_Blocked() {
        Assert.True(MockAccessGuard.Blocks("ei_afx/zoom_zoom", new FakeMode(AppMode.Hosted),
            new FakeUser(UserRole.Contributor)));
    }

    [Fact]
    public void HostedAdmin_Allowed() {
        Assert.False(MockAccessGuard.Blocks("ei_afx/zoom_zoom", new FakeMode(AppMode.Hosted),
            new FakeUser(UserRole.Admin)));
    }

    [Fact]
    public void Local_Allowed() {
        Assert.False(MockAccessGuard.Blocks("ei_afx/zoom_zoom", new FakeMode(AppMode.Local),
            new FakeUser(UserRole.Viewer)));
    }

    [Fact]
    public void UnlistedPath_Allowed() {
        Assert.False(MockAccessGuard.Blocks("ei_afx/config", new FakeMode(AppMode.Hosted),
            new FakeUser(UserRole.Viewer)));
    }
}
