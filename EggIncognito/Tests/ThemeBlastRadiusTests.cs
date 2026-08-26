using System.Reflection;
using EggIdentity.Contract;
using EggIncognito.Controllers;
using EggIncognito.Services;
using EggIncognito.Services.Theme;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EggIncognito.Tests;

public class ThemeBlastRadiusTests {
    [Fact]
    public async Task Resolver_ReturnsNullWithoutADatabase() {
        var resolver = BuildResolver(new AuthenticatedUser());
        Assert.Null(await resolver.ResolveAsync());
    }

    [Fact]
    public void CacheKeys_AreDistinctPerUser() {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        Assert.NotEqual(ThemeResolver.CacheKey(a), ThemeResolver.CacheKey(b));
        Assert.Equal(ThemeResolver.CacheKey(a), ThemeResolver.CacheKey(a));
    }

    [Fact]
    public void NoThemeRoute_TakesAnIdParameter() {
        foreach (var action in typeof(ThemeController)
                     .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)) {
            foreach (var attr in action.GetCustomAttributes<HttpMethodAttribute>()) {
                string template = attr.Template ?? "";
                Assert.DoesNotContain("{id", template);
                Assert.DoesNotContain("{userId", template);
                Assert.DoesNotContain("{owner", template);
            }
        }
    }

    [Fact]
    public void ControllerSource_ScopesEveryStoreReadToTheCaller() {
        string source = File.ReadAllText(Path.Combine(FindRepoRoot(),
            "EggIncognito", "Controllers", "ThemeController.cs"));
        Assert.DoesNotContain("AllAsync", source);
        int getById = CountOccurrences(source, "GetByIdAsync");
        Assert.True(getById <= 1, "GetByIdAsync may appear only in the admin policy read");
        Assert.Contains("store.GetAsync(uid,", source);
    }

    [Fact]
    public void StoredModel_IsJson_NeverSerializedCss() {
        string entity = File.ReadAllText(Path.Combine(FindRepoRoot(),
            "EggIncognito.Data", "Models", "UserTheme.cs"));
        Assert.Contains("public string Model", entity);
        Assert.DoesNotContain("Css", entity);
    }

    private static ThemeResolver BuildResolver(ICurrentUser user) {
        var services = new ServiceCollection().BuildServiceProvider();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var config = new ConfigurationBuilder().Build();
        return new ThemeResolver(user, services, cache, ThemeTestSupport.Serializer(), config);
    }

    private static int CountOccurrences(string haystack, string needle) {
        int count = 0;
        int at = 0;
        while ((at = haystack.IndexOf(needle, at, StringComparison.Ordinal)) >= 0) {
            count++;
            at += needle.Length;
        }

        return count;
    }

    private static string FindRepoRoot() {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null) {
            if (dir.GetFiles("*.slnx").Length > 0 || dir.GetFiles("*.sln").Length > 0) return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("repo root not found");
    }

    private sealed class AuthenticatedUser : ICurrentUser {
        public bool IsAuthenticated => true;
        public Guid? UserId { get; } = Guid.NewGuid();
        public string? DiscordId => null;
        public string? Username => "tester";
        public string? Avatar => null;
        public string? AvatarUrl => null;
        public UserRole Role => UserRole.Viewer;
        public bool IsSupporter => false;
        public bool IsAtLeast(UserRole need) => need == UserRole.Viewer;
    }
}
