namespace EggIncognito.Data.Models;

// Ordered authority levels. Higher ordinal = more authority. Stored as the lowercase name in the
// users.role column; compared by rank so "is at least contributor" is a single check.
public enum UserRole { Viewer = 0, Contributor = 1, Admin = 2 }

public static class UserRoles
{
    // The claim type the role is stamped into the auth cookie under.
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
