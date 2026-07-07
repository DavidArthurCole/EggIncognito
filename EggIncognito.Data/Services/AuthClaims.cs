namespace EggIncognito.Data.Services;

// Shared claim-type constants for provider-neutral identity, referenced by both the Data project
// (UserUpsert stamps it) and the App project (CurrentUser reads it, AuthSetup's Authentik scheme
// stamps it). Lives in Data since App already references Data and Data cannot reference back.
public static class AuthClaims
{
    public const string UserIdClaim = "egi:user_id";
}
