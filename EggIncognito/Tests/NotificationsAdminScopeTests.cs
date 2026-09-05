using Bunit;
using EggIdentity.Contract;
using EggIncognito.Components.Protos;
using EggIncognito.Services;
using EggIncognito.Services.Notifications;
using EggIncognito.Services.Shared;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace EggIncognito.Tests;

public class NotificationsAdminScopeTests : BunitContext {
    private const string CreateForm = "input[aria-label=\"Discord webhook URL\"]";

    private void Wire() {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<ICurrentUser>(new FakeUser());
        Services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor());
        Services.AddSingleton<IWebHostEnvironment>(new FakeWebHostEnvironment());
        Services.AddScoped<NotificationsWorkbenchState>();
        Services.AddScoped<ToastService>();
        Services.AddHttpClient();
    }

    [Fact]
    public async Task AdminScope_HidesCreateUi() {
        Wire();
        var cut = Render<NotificationsWorkbenchModal>(p => p.Add(c => c.AdminScope, true));
        await cut.InvokeAsync(() => cut.Instance.Open());
        cut.WaitForElement(".wb-body");
        Assert.Empty(cut.FindAll(CreateForm));
        Assert.DoesNotContain("New notification", cut.Markup);
    }

    [Fact]
    public async Task SelfScope_KeepsCreateUi() {
        Wire();
        var cut = Render<NotificationsWorkbenchModal>(p => p.Add(c => c.AdminScope, false));
        await cut.InvokeAsync(() => cut.Instance.Open());
        cut.WaitForElement(".wb-body");
        Assert.NotEmpty(cut.FindAll(CreateForm));
        Assert.Contains("New notification", cut.Markup);
    }

    private sealed class FakeUser : ICurrentUser {
        public bool IsAuthenticated => true;
        public Guid? UserId => null;
        public string? DiscordId => "fake";
        public string? Username => "fake";
        public string? Avatar => null;
        public string? AvatarUrl => null;
        public UserRole Role => UserRole.Admin;
        public bool IsSupporter => false;
        public bool IsAtLeast(UserRole need) => UserRole.Admin >= need;
    }
}
