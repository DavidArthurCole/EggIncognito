using System.Text;
using EggIncognito.Core.Services.Devices;
using EggIncognito.Models.Devices;

namespace EggIncognito.Services.Devices;

public static class UiTreeProjector {
    private const int MaxValueLength = 200;
    private const int MaxLabelLength = 72;

    private static readonly (UiSelectorBy By, string Wire, string Label)[] Order = [
        (UiSelectorBy.ResourceId, "ResourceId", "resource-id"),
        (UiSelectorBy.ContentDesc, "ContentDesc", "content-desc"),
        (UiSelectorBy.Text, "Text", "text"),
        (UiSelectorBy.ClassName, "ClassName", "class")
    ];

    public static UiDumpResult Project(UiTree tree) {
        var flat = new List<(UiNode Node, int Depth)>();
        Walk(tree.Root, 0, flat);

        var totals = new Dictionary<(UiSelectorBy, string), int>();
        foreach ((var node, _) in flat) {
            foreach ((var by, _, _) in Order) {
                if (Attr(node, by) is { } value) totals[(by, value)] = totals.GetValueOrDefault((by, value)) + 1;
            }
        }

        var running = new Dictionary<(UiSelectorBy, string), int>();
        var rows = new List<UiNodeRow>(flat.Count);
        int width = 0;
        int height = 0;
        for (int i = 0; i < flat.Count; i++) {
            (var node, int depth) = flat[i];
            width = Math.Max(width, node.Bounds.Right);
            height = Math.Max(height, node.Bounds.Bottom);
            rows.Add(new UiNodeRow(
                i, depth, ShortClass(node.ClassName), RowLabel(node),
                node.ResourceId, node.Text, node.ContentDesc, node.ClassName, node.Package,
                node.Bounds.Left, node.Bounds.Top, node.Bounds.Right, node.Bounds.Bottom,
                node.Clickable, node.Enabled, Hints(node, totals, running)));
        }

        return new UiDumpResult(width, height, rows.Count, rows);
    }

    private static void Walk(UiNode node, int depth, List<(UiNode Node, int Depth)> into) {
        into.Add((node, depth));
        foreach (var child in node.Children) {
            Walk(child, depth + 1, into);
        }
    }

    private static IReadOnlyList<UiSelectorHint> Hints(
        UiNode node,
        Dictionary<(UiSelectorBy, string), int> totals,
        Dictionary<(UiSelectorBy, string), int> running) {
        var hints = new List<UiSelectorHint>(Order.Length);
        foreach ((var by, string wire, string label) in Order) {
            if (Attr(node, by) is not { } value) continue;
            int index = running.GetValueOrDefault((by, value));
            running[(by, value)] = index + 1;
            int matches = totals.GetValueOrDefault((by, value), 1);
            hints.Add(new UiSelectorHint(wire, label, value, index, matches, Snippet(by, wire, value, index)));
        }

        return [.. hints.OrderByDescending(h => h.Unique).ThenBy(h => Rank(h.By))];
    }

    private static int Rank(string wire) {
        for (int i = 0; i < Order.Length; i++) {
            if (Order[i].Wire == wire) return i;
        }

        return Order.Length;
    }

    private static string Snippet(UiSelectorBy by, string wire, string value, int index) {
        string literal = Literal(value);
        if (index > 0) return $"new UiSelector(UiSelectorBy.{wire}, {literal}, Index: {index})";
        return by switch {
            UiSelectorBy.ResourceId => $"UiSelector.Id({literal})",
            UiSelectorBy.ContentDesc => $"UiSelector.Desc({literal})",
            UiSelectorBy.Text => $"UiSelector.Text({literal})",
            _ => $"UiSelector.Class({literal})"
        };
    }

    private static string? Attr(UiNode node, UiSelectorBy by) {
        string? raw = by switch {
            UiSelectorBy.ResourceId => node.ResourceId,
            UiSelectorBy.ContentDesc => node.ContentDesc,
            UiSelectorBy.Text => node.Text,
            UiSelectorBy.ClassName => node.ClassName,
            _ => null
        };
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return raw.Length > MaxValueLength ? null : raw;
    }

    private static string RowLabel(UiNode node) {
        string? primary = First(node.Text, node.ContentDesc, ShortId(node.ResourceId));
        if (primary is null) return ShortClass(node.ClassName);
        primary = primary.ReplaceLineEndings(" ").Trim();
        return primary.Length > MaxLabelLength ? primary[..MaxLabelLength] + "..." : primary;
    }

    private static string? First(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private static string? ShortId(string? resourceId) {
        if (string.IsNullOrWhiteSpace(resourceId)) return null;
        int slash = resourceId.LastIndexOf('/');
        return slash >= 0 && slash < resourceId.Length - 1 ? resourceId[(slash + 1)..] : resourceId;
    }

    private static string ShortClass(string? className) {
        if (string.IsNullOrWhiteSpace(className)) return "node";
        int dot = className.LastIndexOf('.');
        return dot >= 0 && dot < className.Length - 1 ? className[(dot + 1)..] : className;
    }

    private static string Literal(string value) {
        var sb = new StringBuilder(value.Length + 2);
        sb.Append('"');
        foreach (char c in value) {
            switch (c) {
                case '\\':
                    sb.Append("\\\\");
                    break;
                case '"':
                    sb.Append("\\\"");
                    break;
                case '\n':
                    sb.Append("\\n");
                    break;
                case '\r':
                    sb.Append("\\r");
                    break;
                case '\t':
                    sb.Append("\\t");
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }

        sb.Append('"');
        return sb.ToString();
    }
}
