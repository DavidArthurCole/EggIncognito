using EggIdentity.Contract;

namespace EggIncognito.Services.Auth;

public sealed record LocalIdentitySettings(UserRole Role, bool Supporter) {
    public static readonly Guid UserId = new("00000000-0000-4000-8000-000000000001");

    public string RoleName => UserRoles.ToName(Role);
    public string Username => $"local-{RoleName}";

    public static LocalIdentitySettings Bind(IConfiguration config) {
        string? role = config[LocalIdentityGate.RoleKey];
        return new LocalIdentitySettings(
            string.IsNullOrWhiteSpace(role) ? UserRole.Admin : UserRoles.Parse(role),
            config.GetValue(LocalIdentityGate.SupporterKey, true));
    }
}
