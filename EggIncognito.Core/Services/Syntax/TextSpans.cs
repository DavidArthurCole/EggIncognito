namespace EggIncognito.Services.Syntax;

public readonly record struct Span(int Start, int Length, string Class);

public readonly record struct Segment(int Start, int Length, string? Class);

public static class TextSpans {
    private static readonly Span[] NoSpans = [];
    private static readonly Segment[] NoSegments = [];

    public static IReadOnlyList<Span> Empty => NoSpans;

    public static IReadOnlyList<Span> Clean(IReadOnlyList<Span>? spans, int length) {
        if (spans is null || spans.Count == 0 || length <= 0) return NoSpans;
        var kept = new List<Span>(spans.Count);
        foreach (var s in spans) {
            int start = s.Start;
            int end = s.Start + s.Length;
            if (start < 0) start = 0;
            if (end > length) end = length;
            if (end <= start) continue;
            kept.Add(new Span(start, end - start, s.Class));
        }

        if (kept.Count == 0) return NoSpans;
        kept.Sort(CompareByStart);
        var result = new List<Span>(kept.Count);
        int pos = 0;
        foreach (var s in kept) {
            int start = Math.Max(s.Start, pos);
            int end = s.Start + s.Length;
            if (end <= start) continue;
            result.Add(new Span(start, end - start, s.Class));
            pos = end;
        }

        return result;
    }

    public static IReadOnlyList<Span> Layer(IReadOnlyList<Span>? under, IReadOnlyList<Span>? over) => Layer(under, over, int.MaxValue);

    public static IReadOnlyList<Span> Layer(IReadOnlyList<Span>? under, IReadOnlyList<Span>? over, int length) {
        var bottom = Clean(under, length);
        var top = Clean(over, length);
        if (top.Count == 0) return bottom;
        if (bottom.Count == 0) return top;

        var result = new List<Span>(bottom.Count + top.Count);
        int j = 0;
        foreach (var b in bottom) {
            int pos = b.Start;
            int end = b.Start + b.Length;
            while (j < top.Count && top[j].Start + top[j].Length <= pos) j++;
            int k = j;
            while (k < top.Count && top[k].Start < end) {
                var t = top[k];
                if (t.Start > pos) result.Add(new Span(pos, t.Start - pos, b.Class));
                int tEnd = t.Start + t.Length;
                if (tEnd > pos) pos = tEnd;
                if (pos >= end) break;
                k++;
            }

            if (pos < end) result.Add(new Span(pos, end - pos, b.Class));
        }

        result.AddRange(top);
        result.Sort(CompareByStart);
        return result;
    }

    public static IReadOnlyList<Segment> Slice(string? text, IReadOnlyList<Span>? spans) {
        if (string.IsNullOrEmpty(text)) return NoSegments;
        var clean = Clean(spans, text.Length);
        if (clean.Count == 0) return [new Segment(0, text.Length, null)];

        var result = new List<Segment>(clean.Count * 2 + 1);
        int pos = 0;
        foreach (var s in clean) {
            if (s.Start > pos) result.Add(new Segment(pos, s.Start - pos, null));
            result.Add(new Segment(s.Start, s.Length, s.Class));
            pos = s.Start + s.Length;
        }

        if (pos < text.Length) result.Add(new Segment(pos, text.Length - pos, null));
        return result;
    }

    public static List<Span> Find(string? text, IReadOnlyCollection<string>? needles, string cssClass,
        StringComparison comparison = StringComparison.OrdinalIgnoreCase) {
        var found = new List<Span>();
        if (string.IsNullOrEmpty(text) || needles is null || needles.Count == 0) return found;
        foreach (string needle in needles) {
            if (string.IsNullOrEmpty(needle)) continue;
            int from = 0;
            while (from <= text.Length - needle.Length) {
                int idx = text.IndexOf(needle, from, comparison);
                if (idx < 0) break;
                found.Add(new Span(idx, needle.Length, cssClass));
                from = idx + needle.Length;
            }
        }

        return found;
    }

    private static int CompareByStart(Span a, Span b) {
        int byStart = a.Start.CompareTo(b.Start);
        return byStart != 0 ? byStart : b.Length.CompareTo(a.Length);
    }
}
