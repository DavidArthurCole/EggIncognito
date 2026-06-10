using Bunit;
using EggIncognito.Components.Inspector;
using EggIncognito.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace EggIncognito.Tests;

// Phase 4: the Inspector tab ported to Blazor Server. The page is InteractiveServer, but its static
// shell prerenders, so the endpoint list, the send bar (Mock/Live/Custom + Build/Send), the pipeline,
// and the response panel all render on the first server pass. The build/send/decode paths stay in the
// InspectorApiController (salt build, egress, host allowlist); these tests only assert the shell + the
// recursive field-tree component, no live egress.
public class InspectorPageTests
{
    // Page-level: the prerendered /inspector returns 200 with the inspector shell markers.
    public class Integration : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _f;
        public Integration(WebApplicationFactory<Program> f) =>
            _f = f.WithWebHostBuilder(b => b.UseSetting("NoBrowser", "true"));

        [Fact]
        public async Task Inspector_RendersShell()
        {
            var c = _f.CreateClient();
            var r = await c.GetAsync("/inspector");
            Assert.Equal(System.Net.HttpStatusCode.OK, r.StatusCode);
            var html = await r.Content.ReadAsStringAsync();
            // Active nav + the three-pane shell markers.
            Assert.Contains(">Inspector</a>", html);
            Assert.Contains("list-switch", html); // the Endpoints | Objects switch in the left pane
            Assert.Contains("Pipeline", html);
            // Send-target toggle + actions.
            Assert.Contains("target-toggle", html);
            Assert.Contains(">Build</button>", html);
            Assert.Contains(">Send</button>", html);
            // The Live target is present but anonymous; no egress is performed by rendering.
            Assert.Contains("Live API", html);
        }
    }

    // Component-level (bUnit): the recursive FieldTree renders a scalar, an enum select, a repeated
    // editor, and a nested message's child field from a small fake schema. Collect() then walks the
    // edited tree into the protojson object the build call sends.
    public class FieldTreeComponent : BunitContext
    {
        private static SchemaMessage Inner() => new("Inner", new List<SchemaField>
        {
            new("flag", "flag", 1, "bool", false, false, null, null),
        });

        private static SchemaMessage Root() => new("Root", new List<SchemaField>
        {
            new("name", "name", 1, "string", false, false, null, null),
            new("kind", "kind", 2, "enum", false, false, null,
                new List<SchemaEnumValue> { new("A", 0), new("B", 1) }),
            new("ids", "ids", 3, "int32", true, false, null, null),
            new("inner", "inner", 4, "message", false, false, "Inner", null),
        });

        [Fact]
        public void FieldTree_RendersAllFieldKinds()
        {
            var nodes = FieldTreeBuilder.Build(Root(),
                t => t == "Inner" ? Inner() : null);
            var cut = Render<FieldTree>(p => p.Add(c => c.Nodes, nodes));

            // A typed scalar input, an enum + bool select, and the repeated "+ add" button.
            Assert.NotEmpty(cut.FindAll("input.field-input"));
            Assert.NotEmpty(cut.FindAll("select.field-input"));
            Assert.Contains("+ add", cut.Markup);
            // The nested message's child field name appears (recursion resolved Inner's schema).
            Assert.Contains("flag", cut.Markup);
        }

        [Fact]
        public void Collect_WalksEditedTreeIntoProtoJson()
        {
            var nodes = FieldTreeBuilder.Build(Root(),
                t => t == "Inner" ? Inner() : null);
            // Edit the scalar + the nested child + a repeated item.
            nodes.First(n => n.Field.JsonName == "name").Value = "hi";
            nodes.First(n => n.Field.JsonName == "inner").Children
                .First(c => c.Field.JsonName == "flag").Value = "true";
            var ids = nodes.First(n => n.Field.JsonName == "ids");
            ids.Items.Add("7");

            var json = FieldTreeBuilder.Collect(nodes).ToJsonString();
            Assert.Contains("\"name\":\"hi\"", json);
            Assert.Contains("\"flag\":true", json);
            Assert.Contains("\"ids\":[7]", json);
        }
    }
}
