using Bunit;
using EggIncognito.Components.Capture;
using Microsoft.AspNetCore.Mvc.Testing;

namespace EggIncognito.Tests;

// Phase 5: the Capture tab ported to Blazor Server. The page is InteractiveServer, but its static shell
// prerenders, so the stats panel, the flow list container, the toolbar, and the detail pane all render
// on the first server pass, seeded from an empty Hub.Snapshot()/StatsSnapshot(). The live stream moves
// from SSE to a CaptureHub circuit subscription; these tests only assert the prerendered dashboard
// shell (no live device/proxy) plus the ported tree/format render logic at the component level.
public class CapturePageTests
{
    // Page-level: the prerendered /capture returns 200 with the dashboard shell markers.
    public class Integration : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _f;
        public Integration(WebApplicationFactory<Program> f) =>
            _f = f.WithWebHostBuilder(b => b.UseSetting("NoBrowser", "true"));

        [Fact]
        public async Task Capture_RendersShell()
        {
            var c = _f.CreateClient();
            var r = await c.GetAsync("/capture");
            Assert.Equal(System.Net.HttpStatusCode.OK, r.StatusCode);
            var html = await r.Content.ReadAsStringAsync();
            // Active nav + the three panes.
            Assert.Contains(">Capture</a>", html);
            Assert.Contains("id=\"statsPanel\"", html);
            Assert.Contains("id=\"flowsPanel\"", html);
            Assert.Contains("id=\"detailPanel\"", html);
            // The capture toggle (start/stop) + the empty stats pill.
            Assert.Contains("STOPPED", html);
            // Empty dashboard: no flows yet, the device-aware empty state shows.
            Assert.Contains("No device connected yet.", html);
            // The redaction segmented control in the settings popover.
            Assert.Contains("settings-seg", html);
        }
    }

    // Component-level (bUnit): the recursive JsonTree renders nodes for a small object, and FormatView
    // renders a hex dump of a tiny base64 input.
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

            // Keys render as jtree-key spans; the nested object is built.
            Assert.NotEmpty(cut.FindAll(".jtree-key"));
            Assert.Contains("a", cut.Markup);
            Assert.Contains("c", cut.Markup);
            // A leaf value span exists.
            Assert.NotEmpty(cut.FindAll(".jv"));
            // The toolbar controls are present.
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

            // The matching key is wrapped in a <mark>; non-matching branches are dimmed.
            Assert.Contains("<mark>", cut.Markup);
            Assert.Contains("jtree-dim", cut.Markup);
            Assert.Contains("1 match", cut.Markup);
        }

        [Fact]
        public void FormatView_RendersHexOfBytes()
        {
            // base64 "AAEC" -> bytes 00 01 02. Default format is json-tree, so seed hex via View.
            var view = new CaptureViewState { DefaultFormat = "hex" };
            var cut = Render<FormatView>(p => p
                .Add(c => c.Label, "Response")
                .Add(c => c.JsonStr, (string?)null)
                .Add(c => c.RawB64, "AAEC")
                .Add(c => c.View, view));

            // The hex dump shows the offset + bytes.
            Assert.Contains("00000000", cut.Markup);
            Assert.Contains("00 01 02", cut.Markup);
        }
    }

    // Pure converter checks for the ported format.js logic.
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
