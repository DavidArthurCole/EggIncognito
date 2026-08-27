using System.Net;
using Bunit;
using EggIncognito.Components.Protos;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace EggIncognito.Tests;

public class LegalPagesTests {
    private const string Disclaimer =
        "EggIncognito is an independent, fan-made tool and is not affiliated with, endorsed by, or";

    [Collection(SharedAppCollection.Name)]
    public class Routes(SharedAppFactory f) {
        private readonly WebApplicationFactory<Program> _factory = f;

        [Fact]
        public async Task Terms_Route_IsARedirectStub() {
            using var client = _factory.CreateClient();
            var res = await client.GetAsync("/terms");
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            string html = await res.Content.ReadAsStringAsync();
            Assert.DoesNotContain("prose-legal-title", html);
            Assert.DoesNotContain("prose-legal-section", html);
        }

        [Fact]
        public async Task Privacy_Route_IsARedirectStub() {
            using var client = _factory.CreateClient();
            var res = await client.GetAsync("/privacy");
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            string html = await res.Content.ReadAsStringAsync();
            Assert.DoesNotContain("prose-legal-title", html);
            Assert.DoesNotContain("prose-legal-section", html);
        }

        [Fact]
        public async Task LayoutFooter_CarriesDisclaimerAndLegalLinks_OnEveryPage() {
            using var client = _factory.CreateClient();
            foreach (string path in new[] { "/", "/terms", "/privacy", "/support" }) {
                string html = await client.GetStringAsync(path);
                Assert.Equal(1, html.Split("id=\"siteFooter\"").Length - 1);
                Assert.Contains(Disclaimer, html);
                Assert.Contains("href=\"/terms\"", html);
                Assert.Contains("href=\"/privacy\"", html);
            }
        }
    }

    public class Component : BunitContext {
        private void Wire() {
            JSInterop.Mode = JSRuntimeMode.Loose;
            Services.AddSingleton<IWebHostEnvironment>(new FakeWebHostEnvironment());
            Services.AddLogging();
        }

        [Fact]
        public async Task TermsModal_Open_ShowsTheTermsProse() {
            Wire();
            var cut = Render<TermsModal>();
            await cut.InvokeAsync(() => cut.Instance.Open());
            Assert.Equal("Terms of Service", cut.Find(".prose-legal-title").TextContent);
            Assert.Contains("It is not a game client, a cheat, or a service operated by the game's authors.",
                cut.Markup);
        }

        [Fact]
        public async Task PrivacyModal_Open_ShowsThePrivacyProse() {
            Wire();
            var cut = Render<PrivacyModal>();
            await cut.InvokeAsync(() => cut.Instance.Open());
            Assert.Equal("Privacy & Cookies", cut.Find(".prose-legal-title").TextContent);
            Assert.Contains("This service collects as little as possible.", cut.Markup);
        }
    }
}
