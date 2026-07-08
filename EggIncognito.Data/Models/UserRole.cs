namespace EggIncognito.Data.Models;

// Higher ordinal = more authority; compared by rank for "is at least contributor" checks.
public enum UserRole { Viewer = 0, Contributor = 1, Admin = 2 }

public static class UserRoles
{
    public const string ClaimType = "egi:role";

    public static UserRole Parse(string? s) => s?.ToLowerInvariant() switch
    {
        "admin" => UserRole.Admin,
        "contributor" => UserRole.Contributor,
        _ => UserRole.Viewer,
    };

    public static string ToName(UserRole r) => r.ToString().ToLowerInvariant();

    public static bool IsAtLeast(UserRole have, UserRole need) => have >= need;
}
