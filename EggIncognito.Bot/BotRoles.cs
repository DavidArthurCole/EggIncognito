namespace EggIncognito.Bot;
public static class BotRoles
{
    public static bool NeedsRole(IEnumerable<ulong> memberRoleIds, ulong roleId) => !memberRoleIds.Contains(roleId);
}
