using EggIncognito.Services.DataApi;
using Microsoft.AspNetCore.Authentication;

namespace EggIncognito.Services.Auth;

public static class LocalIdentityAuth {
    public const string Scheme = "LocalIdentity";

    public static void AddLocalIdentityAuth(this WebApplicationBuilder builder, LocalIdentitySettings settings) {
        builder.Services.AddSingleton(settings);
        builder.Services.AddAuthentication(Scheme)
            .AddScheme<AuthenticationSchemeOptions, LocalIdentityAuthenticationHandler>(Scheme, null)
            .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyGen.SchemeName, null);
        builder.Services.AddAuthorization();
    }
}
