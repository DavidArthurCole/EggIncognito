using System.Security.Claims;
using EggIncognito.Data.Models;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EggIncognito.Data.Services;

// Upserts the User row on each Discord login. Wired as the AddDiscord OnCreatingTicket handler. The
// pure helpers are unit-tested; OnLoginAsync resolves the scoped DbContext from the ticket's request
// services and does the DB pass. A DB failure here fails the login, so we never issue a cookie for a
// user with no row, which the ACL lookup depends on.
public static class UserUpsert
{
    public sealed record Info(string DiscordId, string Username, string? Avatar);

    // Discord's avatar hash claim type as emitted by AspNet.Security.OAuth.Discord.
    private const string AvatarClaim = "urn:discord:avatar:hash";

    public static Info Extract(ClaimsPrincipal principal)
    {
        var id = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var name = principal.FindFirstValue(ClaimTypes.Name) ?? "";
        var avatar = principal.FindFirstValue(AvatarClaim);
        return new Info(id, name, string.IsNullOrEmpty(avatar) ? null : avatar);
    }

    public static void Apply(User row, Info info, bool isNew, DateTimeOffset now)
    {
        if (isNew)
        {
            row.DiscordId = info.DiscordId;
            row.CreatedAt = now; // overridden by the column default on insert; set for in-memory tests
        }
        row.Username = info.Username;
        row.Avatar = info.Avatar;
        row.LastLoginAt = now;
    }

    // The role a user should have after this login: allowlisted ids are re-promoted to admin; otherwise
    // a new user defaults to viewer and a returning user keeps their stored role.
    public static string ResolveRole(string? existingRole, string discordId, AdminAllowlist allow)
    {
        if (allow.Ids.Contains(discordId)) return UserRoles.ToName(UserRole.Admin);
        return existingRole ?? UserRoles.ToName(UserRole.Viewer);
    }

    public static async Task OnLoginAsync(OAuthCreatingTicketContext ctx)
    {
        var info = Extract(ctx.Principal!);
        if (string.IsNullOrEmpty(info.DiscordId)) return;

        var sp = ctx.HttpContext.RequestServices;
        var db = sp.GetRequiredService<EggIncognitoDbContext>();
        var allow = sp.GetRequiredService<AdminAllowlist>();

        var existing = await db.Users.FirstOrDefaultAsync(u => u.DiscordId == info.DiscordId);
        var now = DateTimeOffset.UtcNow;
        var role = ResolveRole(existing?.Role, info.DiscordId, allow);
        if (existing is null)
        {
            var row = new User { Role = role };
            Apply(row, info, isNew: true, now);
            db.Users.Add(row);
        }
        else
        {
            existing.Role = role;
            Apply(existing, info, isNew: false, now);
        }
        await db.SaveChangesAsync();

        // Bake the role into the issued cookie so per-request authorization needs no DB hit.
        (ctx.Identity as System.Security.Claims.ClaimsIdentity)?
            .AddClaim(new System.Security.Claims.Claim(UserRoles.ClaimType, role));
    }
}
