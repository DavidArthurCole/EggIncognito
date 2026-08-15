using EggIdentity.Contract;
using EggIncognito.Data.Services;
using Microsoft.Extensions.Caching.Memory;

namespace EggIncognito.Services.Theme;

public sealed record ResolvedTheme(string Css, bool HueRotation);

public sealed class ThemeResolver(
    ICurrentUser currentUser,
    IServiceProvider services,
    IMemoryCache cache,
    ThemeCssSerializer serializer,
    IConfiguration configuration) {
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    public static string CacheKey(Guid userId) => $"egi.theme.{userId:N}";

    public static void Invalidate(IMemoryCache cache, Guid userId) => cache.Remove(CacheKey(userId));

    public async Task<ResolvedTheme?> ResolveAsync(CancellationToken ct = default) {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not { } uid) return null;
        if (services.GetService(typeof(UserThemeStore)) is not UserThemeStore store) return null;

        if (cache.TryGetValue(CacheKey(uid), out ResolvedTheme? cached)) return cached;

        var resolved = await ResolveUncachedAsync(store, uid, ct);
        cache.Set(CacheKey(uid), resolved, CacheTtl);
        return resolved;
    }

    private async Task<ResolvedTheme?> ResolveUncachedAsync(UserThemeStore store, Guid uid, CancellationToken ct) {
        var row = await store.ActiveForAsync(uid, ct);
        bool isDefaultTheme = false;
        if (row is null) {
            var policy = await store.GetPolicyAsync(ct);
            if (policy.DefaultThemeId is not { } defaultId) return null;
            row = await store.GetByIdAsync(defaultId, ct);
            if (row is null) return null;
            isDefaultTheme = true;
        }

        var (model, _) = ThemeModel.Parse(row.Model);
        if (model is null) return null;
        if (isDefaultTheme && !string.IsNullOrEmpty(model.Css)) model = model with { Css = "" };

        bool customCssAllowed = !isDefaultTheme && await CustomCssAllowedAsync(store, ct);
        string css = serializer.Serialize(model, ThemeScope.Live, customCssAllowed);
        if (css.Length == 0) return null;
        return new ResolvedTheme(css, ThemeCssSerializer.UsesHueRotation(model));
    }

    private async Task<bool> CustomCssAllowedAsync(UserThemeStore store, CancellationToken ct) {
        if (!configuration.GetValue("Theme:CustomCss", true)) return false;
        if (!currentUser.IsAtLeast(UserRole.Contributor)) return false;
        var policy = await store.GetPolicyAsync(ct);
        return policy.CustomCssEnabled;
    }
}
