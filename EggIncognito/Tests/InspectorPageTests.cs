using System.Net;
using System.Text.Json.Nodes;
using Bunit;
using EggIncognito.Components.Inspector;
using EggIncognito.Services;
using Microsoft.AspNetCore.Mvc.Testing;

namespace EggIncognito.Tests;

public class InspectorPageTests {
    [Collection(SharedAppCollection.Name)]
    public class Integration(SharedAppFactory f) {
        private readonly WebApplicationFactory<Program> _f = f;

        [Fact]
        public async Task Inspector_RendersShell() {
            var c = _f.CreateClient();
            var r = await c.GetAsync("/inspector");
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            string html = await r.Content.ReadAsStringAsync();

            Assert.Contains(">Inspector</a>", html);
            Assert.Contains("list-switch", html);
            Assert.Contains("Pipeline", html);

            Assert.Contains("target-toggle", html);
            Assert.Contains(">Build</button>", html);
            Assert.Contains(">Send</button>", html);

            Assert.Contains("Live API", html);
        }
    }


    public class FieldTreeComponent : BunitContext {
        private static SchemaMessage Inner() => new("Inner", new List<SchemaField> {
            new("flag", "flag", 1, "bool", false, false, null, null)
        });

        private static SchemaMessage Root() => new("Root", new List<SchemaField> {
            new("name", "name", 1, "string", false, false, null, null),
            new("kind", "kind", 2, "enum", false, false, null,
                new List<SchemaEnumValue> { new("A", 0), new("B", 1) }),
            new("ids", "ids", 3, "int32", true, false, null, null),
            new("inner", "inner", 4, "message", false, false, "Inner", null)
        });

        [Fact]
        public void FieldTree_RendersAllFieldKinds() {
            var nodes = FieldTreeBuilder.Build(Root(),
                t => t == "Inner" ? Inner() : null);
            var cut = Render<FieldTree>(p => p.Add(c => c.Nodes, nodes));


            Assert.NotEmpty(cut.FindAll("input.field-input"));
            Assert.NotEmpty(cut.FindAll("select.field-input"));
            Assert.Contains("+ add", cut.Markup);

            Assert.Contains("flag", cut.Markup);
        }

        [Fact]
        public void Collect_WalksEditedTreeIntoProtoJson() {
            var nodes = FieldTreeBuilder.Build(Root(),
                t => t == "Inner" ? Inner() : null);

            nodes.First(n => n.Field.JsonName == "name").Value = "hi";
            nodes.First(n => n.Field.JsonName == "inner").Children
                .First(c => c.Field.JsonName == "flag").Value = "true";
            var ids = nodes.First(n => n.Field.JsonName == "ids");
            ids.Items.Add("7");

            string json = FieldTreeBuilder.Collect(nodes).ToJsonString();
            Assert.Contains("\"name\":\"hi\"", json);
            Assert.Contains("\"flag\":true", json);
            Assert.Contains("\"ids\":[7]", json);
        }


        [Fact]
        public void Apply_MapsRawJsonBackOntoTree() {
            var nodes = FieldTreeBuilder.Build(Root(),
                t => t == "Inner" ? Inner() : null);
            var obj = (JsonObject)JsonNode.Parse("{\"name\":\"hi\",\"ids\":[3,4],\"inner\":{\"flag\":true}}")!;

            FieldTreeBuilder.Apply(nodes, obj);

            Assert.Equal("hi", nodes.First(n => n.Field.JsonName == "name").Value);
            Assert.Equal(new[] { "3", "4" }, nodes.First(n => n.Field.JsonName == "ids").Items);
            Assert.Equal("true", nodes.First(n => n.Field.JsonName == "inner").Children
                .First(c => c.Field.JsonName == "flag").Value);


            FieldTreeBuilder.Apply(nodes, []);
            Assert.Equal("", nodes.First(n => n.Field.JsonName == "name").Value);
            Assert.Empty(nodes.First(n => n.Field.JsonName == "ids").Items);
            Assert.Equal("", nodes.First(n => n.Field.JsonName == "inner").Children
                .First(c => c.Field.JsonName == "flag").Value);
        }
    }
}
