namespace EggIncognito.Data.Services;

// The set of Discord ids auto-promoted to admin on login, from Discord:AdminIds config. Bootstraps the
// first admin without DB editing; the allowlist stays authoritative for admin, so a returning
// allowlisted user is re-promoted on login even if demoted in the UI.
public sealed record AdminAllowlist(IReadOnlySet<string> Ids)
{
    public static AdminAllowlist FromConfig(string? csv)
    {
        var ids = (csv ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);
        return new AdminAllowlist(ids);
    }
}
