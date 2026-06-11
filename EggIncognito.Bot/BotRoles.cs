namespace EggIncognito.Bot;

// Pure role-decision helpers, unit-tested without a live gateway.
public static class BotRoles
{
    // True when roleId is absent from the member's current role IDs.
    public static bool NeedsRole(IEnumerable<ulong> memberRoleIds, ulong roleId) => !memberRoleIds.Contains(roleId);
}
