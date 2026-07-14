using Bunit;
using EggIncognito.Capture;
using EggIncognito.Components.Capture;
using Microsoft.AspNetCore.Mvc.Testing;

namespace EggIncognito.Tests;

public class CapturePageTests
{
    [Collection(SharedAppCollection.Name)]
    public class Integration
    {
        private readonly WebApplicationFactory<Program> _f;
        public Integration(SharedAppFactory f) => _f = f;

        [Fact]
        public async Task Capture_RendersShell()
        {
            var c = _f.CreateClient();
            var r = await c.GetAsync("/capture");
            Assert.Equal(System.Net.HttpStatusCode.OK, r.StatusCode);
            var html = await r.Content.ReadAsStringAsync();
            Assert.Contains(">Capture</a>", html);
            Assert.Contains("id=\"statsPanel\"", html);
            Assert.Contains("id=\"flowsPanel\"", html);
            Assert.Contains("id=\"detailPanel\"", html);
            Assert.Contains("STOPPED", html);
            Assert.Contains("No device connected yet.", html);
            Assert.Contains("settings-seg", html);
        }
    }

    public class Components : BunitContext
    {
        [Fact]
        public void JsonTree_RendersNodesForObject()
        {
            var root = TreeNode.Parse("{\"a\":1,\"b\":{\"c\":\"hi\"}}");
            Assert.NotNull(root);
            var cut = Render<JsonTree>(p => p
                .Add(c => c.Root, root)
                .Add(c => c.View, new CaptureViewState()));

            Assert.NotEmpty(cut.FindAll(".jtree-key"));
            Assert.Contains("a", cut.Markup);
            Assert.Contains("c", cut.Markup);
            Assert.NotEmpty(cut.FindAll(".jv"));
            Assert.Contains("Expand all", cut.Markup);
        }

        [Fact]
        public void JsonTree_Search_MarksMatchesAndDimsOthers()
        {
            var root = TreeNode.Parse("{\"alpha\":1,\"beta\":2}");
            var cut = Render<JsonTree>(p => p
                .Add(c => c.Root, root)
                .Add(c => c.View, new CaptureViewState()));

            cut.Find(".jtree-search").Input("alpha");

            // Search is debounced (~250ms) then re-renders, so poll instead of asserting synchronously.
            cut.WaitForAssertion(() =>
            {
                Assert.Contains("<mark>", cut.Markup);
                Assert.Contains("jtree-dim", cut.Markup);
                Assert.Contains("1 match", cut.Markup);
            }, TimeSpan.FromSeconds(2));
        }

        [Fact]
        public void FormatView_BlurredValue_TogglesRevealOnClick()
        {
            var view = new CaptureViewState { RedactionMode = "blur", DefaultFormat = "json" };
            var cut = Render<FormatView>(p => p
                .Add(c => c.Label, "Request")
                .Add(c => c.JsonStr, "{\"eiUserId\":\"EI1234567890\"}")
                .Add(c => c.View, view));

            var span = cut.Find(".blurred");
            Assert.DoesNotContain("revealed", span.GetAttribute("class"));
            span.Click();
            Assert.Contains("revealed", cut.Find(".blurred").GetAttribute("class"));
            cut.Find(".blurred").Click();
            Assert.DoesNotContain("revealed", cut.Find(".blurred").GetAttribute("class"));
        }

        [Fact]
        public void FlowList_AutoScroll_ScrollsToNewestOnNewFlow()
        {
            var module = JSInterop.SetupModule("./interop/scroll.js");
            module.SetupVoid("scrollToBottom", _ => true);

            var flows = new List<DashboardFlow>
            {
                new(1, "12:00:00", "ei/first_contact", "POST", 200, null, null, "", null),
            };
            var cut = Render<FlowList>(p => p
                .Add(c => c.Flows, flows)
                .Add(c => c.View, new CaptureViewState { AutoScroll = true }));

            cut.WaitForAssertion(() => module.VerifyInvoke("scrollToBottom"));
        }

        [Fact]
        public void FlowList_AutoScrollOff_DoesNotScroll()
        {
            var module = JSInterop.SetupModule("./interop/scroll.js");
            module.SetupVoid("scrollToBottom", _ => true);

            var flows = new List<DashboardFlow>
            {
                new(1, "12:00:00", "ei/first_contact", "POST", 200, null, null, "", null),
            };
            Render<FlowList>(p => p
                .Add(c => c.Flows, flows)
                .Add(c => c.View, new CaptureViewState { AutoScroll = false }));

            module.VerifyNotInvoke("scrollToBottom");
        }

        [Fact]
        public void FormatView_RendersHexOfBytes()
        {
            var view = new CaptureViewState { DefaultFormat = "hex" };
            var cut = Render<FormatView>(p => p
                .Add(c => c.Label, "Response")
                .Add(c => c.JsonStr, (string?)null)
                .Add(c => c.RawB64, "AAEC")
                .Add(c => c.View, view));

            Assert.Contains("00000000", cut.Markup);
            Assert.Contains("00 01 02", cut.Markup);
        }
    }

    public class Format
    {
        [Fact]
        public void Yaml_RendersNestedObject()
        {
            var y = CaptureFormat.JsonToText("{\"a\":1,\"b\":{\"c\":2}}", "yaml");
            Assert.Contains("a: 1", y);
            Assert.Contains("b:", y);
            Assert.Contains("c: 2", y);
        }

        [Fact]
        public void Hex_EmptyInput_ReportsEmpty()
        {
            Assert.Equal("(empty)", CaptureFormat.BytesToText("", "hex"));
        }

        [Fact]
        public void Xml_WrapsAndPrettyPrints()
        {
            var x = CaptureFormat.JsonToText("{\"a\":1}", "xml");
            Assert.Contains("<root>", x);
            Assert.Contains("<a>1</a>", x);
        }
    }
}
