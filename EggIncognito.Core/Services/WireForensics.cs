using Google.Protobuf.Reflection;

namespace EggIncognito.Services;

// Schema-less + schema-aware protobuf wire-format diagnosis for corrupt blobs. Walks raw bytes by wire
// type (does NOT depend on a successful parse), records the exact byte offset where the structure first
// breaks, resolves numeric field paths to names when a root type + reflection are supplied, flags
// wire-type-vs-schema mismatches, and salvages printable-ASCII runs from the broken span. Ported in
// concept (not code) from EggIncAPITools' walkProtoWire / traceFailingField / resolveFieldPath; C#-native
// because CodedInputStream is sealed and cannot be hooked the jspb way.
public static class WireForensics
{
    public sealed record WireError(int Offset, string Path, string? ResolvedPath, string Message);
    public sealed record HexWindow(int From, int To, int ErrorIndexInWindow, string Hex);
    public sealed record SalvagedString(int Offset, string Text);

    // DataStart/DataEnd = the exact LEN payload byte range [DataStart, DataEnd); null for non-LEN
    // fields and for LEN fields whose declared length overruns the region.
    public sealed record WireNode(
        string Path,
        string? ResolvedName,
        int Field,
        string Wire,
        int Offset,
        int? Len,
        int? DataStart,
        int? DataEnd,
        bool SchemaMismatch,
        IReadOnlyList<WireNode> Children);

    // One field recovered by the tolerant re-parse (v2). Value is best-effort: scalar text/number, a
    // decoded string, or "<N bytes>" for non-printable blobs. Bad = the field did not decode cleanly.
    public sealed record RecoveredField(int Field, string? ResolvedName, string Wire, string Value, bool Bad);

    // Result of recovering one corrupt record's fields. AlignedAt is the byte offset the re-parse locked
    // onto; SkippedBytes is how many bytes it had to step over to resync.
    public sealed record Recovery(
        int AlignedAt,
        int SkippedBytes,
        IReadOnlyList<RecoveredField> Fields);

    public sealed record DiagnoseResult(
        bool Ok,
        int TotalLen,
        int NodesWalked,
        WireError? FirstError,
        IReadOnlyList<WireError> AllErrors,
        HexWindow? HexAround,
        IReadOnlyList<SalvagedString> Salvaged,
        IReadOnlyList<WireNode> Tree,
        Recovery? Recovered);

    static readonly string[] WireNames = ["varint", "i64", "len", "sgroup", "egroup", "i32", "?6", "?7"];

    const int MaxDepth = 64;
    const int MaxNodes = 200_000;

    sealed class Ctx
    {
        public readonly List<WireError> Errors = [];
        public int NodeCount;
        public void Record(string path, string? resolved, int offset, string message) =>
            Errors.Add(new WireError(offset, path, resolved, message));
    }

    sealed class WireException(string message, int offset) : System.Exception(message)
    {
        public int Offset { get; } = offset;
    }

    // Read a base-128 varint at pos. Returns (value, next). Throws on truncation/overlong.
    static (ulong Value, int Next) ReadVarint(ReadOnlySpan<byte> buf, int pos)
    {
        ulong result = 0;
        int shift = 0;
        int start = pos;
        while (pos < buf.Length)
        {
            byte b = buf[pos++];
            result |= (ulong)(b & 0x7f) << shift;
            if ((b & 0x80) == 0) return (result, pos);
            shift += 7;
            if (shift > 63) throw new WireException("varint too long (>10 bytes)", start);
        }
        throw new WireException("truncated varint (hit end of buffer)", start);
    }

    // Guard for LEN payload lengths: true when the declared length fits in [pos, end). Checked as ulong
    // BEFORE narrowing to int so an oversized varint cannot wrap negative and defeat the bounds check
    // (out-of-bounds slices / backward re-parse loops).
    static bool LenFits(ulong declaredLen, int pos, int end) =>
        pos <= end && declaredLen <= (ulong)(end - pos);

    // Heuristic: does a LEN payload [start,end) parse cleanly as a nested message (descend) or is it a
    // leaf string/bytes/packed (don't descend)? Clean = every field reads to exactly end.
    static bool LooksLikeMessage(ReadOnlySpan<byte> buf, int start, int end)
    {
        if (start == end) return false;
        int pos = start;
        try
        {
            while (pos < end)
            {
                var (tag, next) = ReadVarint(buf, pos);
                pos = next;
                int wire = (int)(tag & 0x7);
                int field = (int)(tag >> 3);
                if (field == 0) return false;
                switch (wire)
                {
                    case 0: pos = ReadVarint(buf, pos).Next; break;
                    case 1: pos += 8; break;
                    case 5: pos += 4; break;
                    case 2:
                        var (len, lnext) = ReadVarint(buf, pos);
                        if (!LenFits(len, lnext, end)) return false;
                        pos = lnext + (int)len;
                        break;
                    default: return false; // groups / illegal => bail
                }
                if (pos > end) return false;
            }
            return pos == end;
        }
        catch { return false; }
    }

    static List<WireNode> WalkMessage(ReadOnlySpan<byte> buf, int start, int end, string path, int depth, Ctx ctx)
    {
        var nodes = new List<WireNode>();
        int pos = start;
        while (pos < end)
        {
            int fieldOffset = pos;
            ulong tag;
            try { (tag, pos) = ReadVarint(buf, pos); }
            catch (WireException e) { ctx.Record(path, null, e.Offset, e.Message); break; }

            int wire = (int)(tag & 0x7);
            int field = (int)(tag >> 3);
            string fieldPath = path.Length == 0 ? field.ToString() : $"{path}.{field}";

            if (field == 0) { ctx.Record(fieldPath, null, fieldOffset, "field number 0 (illegal)"); break; }

            int? len = null;
            int? dataStart = null, dataEnd = null;
            List<WireNode> children = [];
            try
            {
                switch (wire)
                {
                    case 0: pos = ReadVarint(buf, pos).Next; break;
                    case 1:
                        pos += 8;
                        if (pos > end) throw new WireException("i64 overruns region", fieldOffset);
                        break;
                    case 5:
                        pos += 4;
                        if (pos > end) throw new WireException("i32 overruns region", fieldOffset);
                        break;
                    case 2:
                        var (l, bodyStart) = ReadVarint(buf, pos);
                        if (l <= int.MaxValue) len = (int)l;
                        if (!LenFits(l, bodyStart, end))
                            throw new WireException($"len-field declares {l} bytes but only {System.Math.Max(0, end - bodyStart)} remain (overrun)", fieldOffset);
                        int bodyEnd = bodyStart + (int)l;
                        dataStart = bodyStart;
                        dataEnd = bodyEnd;
                        if (depth < MaxDepth && ctx.NodeCount < MaxNodes && LooksLikeMessage(buf, bodyStart, bodyEnd))
                            children = WalkMessage(buf, bodyStart, bodyEnd, fieldPath, depth + 1, ctx);
                        pos = bodyEnd;
                        break;
                    default:
                        throw new WireException($"illegal wire type {wire}", fieldOffset);
                }
            }
            catch (WireException e)
            {
                ctx.Record(fieldPath, null, e.Offset, e.Message);
                nodes.Add(new WireNode(fieldPath, null, field, WireNames[wire], fieldOffset, len, dataStart, dataEnd, false, children));
                break;
            }

            ctx.NodeCount++;
            nodes.Add(new WireNode(fieldPath, null, field, WireNames[wire], fieldOffset, len, dataStart, dataEnd, false, children));
            if (ctx.NodeCount >= MaxNodes) break;
        }
        return nodes;
    }

    static HexWindow BuildHexWindow(ReadOnlySpan<byte> buf, int errorOffset)
    {
        int lo = System.Math.Max(0, errorOffset - 16);
        int hi = System.Math.Min(buf.Length, errorOffset + 16);
        return new HexWindow(lo, hi, errorOffset - lo, System.Convert.ToHexString(buf[lo..hi]).ToLowerInvariant());
    }

    // Printable-ASCII run scan over [start,end). minLen filters noise. No local function so the ref-like
    // span is not captured (CS9108).
    static List<SalvagedString> SalvageStrings(ReadOnlySpan<byte> buf, int start, int end, int minLen = 4)
    {
        var outp = new List<SalvagedString>();
        int runLen = 0;
        int from = System.Math.Max(0, start), to = System.Math.Min(buf.Length, end);
        for (int i = from; i <= to; i++)
        {
            bool printable = i < to && buf[i] >= 0x20 && buf[i] <= 0x7e;
            if (printable) { runLen++; continue; }
            if (runLen >= minLen)
                outp.Add(new SalvagedString(i - runLen, System.Text.Encoding.Latin1.GetString(buf.Slice(i - runLen, runLen))));
            runLen = 0;
        }
        return outp;
    }

    public static DiagnoseResult Diagnose(ReadOnlySpan<byte> bytes, string? rootTypeName, IProtoReflection? reflection)
    {
        var ctx = new Ctx();
        IReadOnlyList<WireNode> tree = WalkMessage(bytes, 0, bytes.Length, "", 0, ctx);

        WireError? first = ctx.Errors.Count == 0
            ? null
            : ctx.Errors.Aggregate((a, b) => b.Offset < a.Offset ? b : a);

        HexWindow? hex = first is null ? null : BuildHexWindow(bytes, first.Offset);

        List<SalvagedString> salvaged = [];
        if (first is not null)
        {
            int lo = System.Math.Max(0, first.Offset - 64);
            int hi = System.Math.Min(bytes.Length, first.Offset + 256);
            salvaged = SalvageStrings(bytes, lo, hi);
        }

        MessageDescriptor? rootDesc = null;
        if (rootTypeName is not null && reflection is not null)
        {
            rootDesc = reflection.FindMessage(rootTypeName);
            tree = ResolveSchema(tree, rootTypeName, reflection);
            if (first is not null)
            {
                var resolvedPath = FindResolvedPath(tree, first.Path);
                if (resolvedPath is not null) first = first with { ResolvedPath = resolvedPath };
            }
        }

        // v2: tolerant field-by-field recovery of the broken record. Re-parse the region around the break
        // skipping corrupt fields (the way the real parser cannot) so the intact fields past the corruption
        // are still readable. Recover the enclosing record's body, then resolve numbers to names if a schema
        // path led there.
        Recovery? recovered = null;
        if (first is not null)
        {
            int recoverStart = EnclosingRegionStart(tree, first.Offset, 0);
            int recoverEnd = System.Math.Min(bytes.Length, first.Offset + 4096);
            var (fields, aligned, skipped) = RecoverFields(bytes, recoverStart, recoverEnd);
            if (fields.Count > 0)
            {
                var resolved = ResolveRecovered(fields, first.Path, rootDesc);
                recovered = new Recovery(aligned, skipped, resolved);
            }
        }

        return new DiagnoseResult(
            Ok: ctx.Errors.Count == 0,
            TotalLen: bytes.Length,
            NodesWalked: ctx.NodeCount,
            FirstError: first,
            AllErrors: ctx.Errors,
            HexAround: hex,
            Salvaged: salvaged,
            Tree: tree,
            Recovered: recovered);
    }

    // Find the start offset of the deepest walked region (message body) that contains errorOffset, so
    // recovery re-parses that record rather than the whole buffer. Falls back to the outermost start.
    // Uses the node's exact payload range [DataStart, DataEnd); an error at DataEnd is the next sibling
    // field's tag and belongs to the PARENT region, not this body.
    static int EnclosingRegionStart(IReadOnlyList<WireNode> tree, int errorOffset, int fallbackStart)
    {
        foreach (var n in tree)
        {
            if (n.DataStart is int ds && n.DataEnd is int de && n.Children.Count > 0
                && errorOffset >= ds && errorOffset < de)
                return EnclosingRegionStart(n.Children, errorOffset, ds);
        }
        return fallbackStart;
    }

    // One field read attempt at pos. Returns (node, next) or null if the bytes there are not a plausible
    // field. Ported from traceFailingField.js tryField. No span capture (static, span passed in).
    static (RecoveredField Node, int Next)? TryField(ReadOnlySpan<byte> buf, int pos, int end)
    {
        ulong tag;
        try { (tag, pos) = ReadVarint(buf, pos); } catch { return null; }
        int wire = (int)(tag & 0x7);
        int field = (int)(tag >> 3);
        if (field == 0 || field > 0x1fffffff) return null;

        switch (wire)
        {
            case 0:
                ulong v;
                try { (v, pos) = ReadVarint(buf, pos); } catch { return null; }
                return (new RecoveredField(field, null, "varint", v.ToString(), false), pos);
            case 1:
                if (pos + 8 > end) return null;
                double d = System.BitConverter.ToDouble(buf.Slice(pos, 8));
                return (new RecoveredField(field, null, "i64", d.ToString(System.Globalization.CultureInfo.InvariantCulture), false), pos + 8);
            case 5:
                if (pos + 4 > end) return null;
                float fl = System.BitConverter.ToSingle(buf.Slice(pos, 4));
                return (new RecoveredField(field, null, "i32", fl.ToString(System.Globalization.CultureInfo.InvariantCulture), false), pos + 4);
            case 2:
                ulong lenRaw;
                try { (lenRaw, pos) = ReadVarint(buf, pos); } catch { return null; }
                if (!LenFits(lenRaw, pos, end)) return null;
                int len = (int)lenRaw;
                var slice = buf.Slice(pos, len);
                bool printable = len > 0 && IsPrintable(slice);
                var node = printable
                    ? new RecoveredField(field, null, "len", System.Text.Encoding.UTF8.GetString(slice), false)
                    : new RecoveredField(field, null, "len", $"<{len} bytes>", true);
                return (node, pos + len);
            default:
                return null; // wire 3/4/6/7 -> implausible here
        }
    }

    static bool IsPrintable(ReadOnlySpan<byte> s)
    {
        foreach (var c in s)
            if (!(c == 0x09 || c == 0x0a || (c >= 0x20 && c <= 0x7e))) return false;
        return true;
    }

    // Read fields linearly from `from`, resyncing by one byte when a field does not parse, until `end`.
    // Returns (fields, cleanPrefixCount, skippedBytes). Ported from traceFailingField.js linear().
    static (List<RecoveredField> Fields, int CleanPrefix, int Skipped) RecoverLinear(ReadOnlySpan<byte> buf, int from, int end)
    {
        var fields = new List<RecoveredField>();
        int pos = from, skip = 0, cleanPrefix = 0;
        bool sawSkip = false;
        while (pos < end)
        {
            var r = TryField(buf, pos, end);
            if (r is { } hit)
            {
                fields.Add(hit.Node);
                if (!sawSkip) cleanPrefix++;
                pos = hit.Next;
                continue;
            }
            sawSkip = true;
            skip++; pos++;
            if (skip > 4096) break;
        }
        return (fields, cleanPrefix, skip);
    }

    // Tolerant recovery of a message region. The caller's start may sit on a tag, a length varint, or a
    // byte or two into a wrapper; try a few candidate offsets and keep the one whose fields parse cleanly
    // from the very first byte (longest clean prefix), not the one that racks up the most fields after
    // wandering through garbage. Ported from traceFailingField.js recoverMessageFields.
    static (IReadOnlyList<RecoveredField> Fields, int AlignedAt, int Skipped) RecoverFields(ReadOnlySpan<byte> buf, int start, int end, int maxProbe = 8)
    {
        (List<RecoveredField> f, int prefix, int skip, int off)? best = null;
        int hi = System.Math.Min(start + maxProbe, end - 1);
        for (int off = start; off <= hi; off++)
        {
            var (f, prefix, skip) = RecoverLinear(buf, off, end);
            int score = prefix * 1000 - off; // prefer earlier offset on ties
            int bestScore = best is { } b ? b.prefix * 1000 - b.off : int.MinValue;
            if (best is null || score > bestScore) best = (f, prefix, skip, off);
        }
        return best is { } bb ? (bb.f, bb.off, bb.skip) : ([], start, 0);
    }

    // Map recovered field numbers to names using the message type that the broken path led to. The path's
    // last segment is the broken field; its PARENT message type owns the sibling fields we recovered.
    static IReadOnlyList<RecoveredField> ResolveRecovered(IReadOnlyList<RecoveredField> fields, string brokenPath, MessageDescriptor? rootDesc)
    {
        if (rootDesc is null) return fields;
        // Walk the numeric path down to the parent of the broken field.
        var segs = brokenPath.Split('.').Select(int.Parse).ToArray();
        var desc = rootDesc;
        for (int i = 0; i < segs.Length - 1 && desc is not null; i++)
        {
            var fd = desc.Fields.InFieldNumberOrder().FirstOrDefault(f => f.FieldNumber == segs[i]);
            desc = fd is { FieldType: FieldType.Message or FieldType.Group } ? fd.MessageType : null;
        }
        if (desc is null) return fields;
        var parent = desc;
        return fields.Select(rf =>
        {
            var fd = parent.Fields.InFieldNumberOrder().FirstOrDefault(f => f.FieldNumber == rf.Field);
            return rf with { ResolvedName = fd?.Name };
        }).ToList();
    }

    // Expected wire type for a schema field type. 0=varint,1=i64,2=len,5=i32. -1 = unknown.
    static int ExpectedWire(FieldDescriptor f)
    {
        if (f.IsRepeated && f.IsPacked) return 2; // packed repeated scalars travel as LEN
        return f.FieldType switch
        {
            FieldType.Int32 or FieldType.Int64 or FieldType.UInt32 or FieldType.UInt64
                or FieldType.SInt32 or FieldType.SInt64 or FieldType.Bool or FieldType.Enum => 0,
            FieldType.Fixed64 or FieldType.SFixed64 or FieldType.Double => 1,
            FieldType.Fixed32 or FieldType.SFixed32 or FieldType.Float => 5,
            FieldType.String or FieldType.Bytes or FieldType.Message or FieldType.Group => 2,
            _ => -1,
        };
    }

    static int WireIndex(string wireName) => System.Array.IndexOf(WireNames, wireName);

    static IReadOnlyList<WireNode> ResolveSchema(IReadOnlyList<WireNode> tree, string rootTypeName, IProtoReflection reflection)
    {
        var rootDesc = reflection.FindMessage(rootTypeName);
        if (rootDesc is null) return tree;
        return tree.Select(n => ResolveNode(n, rootDesc)).ToList();
    }

    static WireNode ResolveNode(WireNode node, MessageDescriptor desc)
    {
        var fd = desc.Fields.InFieldNumberOrder().FirstOrDefault(f => f.FieldNumber == node.Field);
        if (fd is null)
            return node with { ResolvedName = "<unknown>" };

        int expected = ExpectedWire(fd);
        int actual = WireIndex(node.Wire);
        bool mismatch = expected >= 0 && actual >= 0 && expected != actual;

        IReadOnlyList<WireNode> children = node.Children;
        if (node.Children.Count > 0 && (fd.FieldType == FieldType.Message || fd.FieldType == FieldType.Group))
        {
            var childDesc = fd.MessageType;
            children = node.Children.Select(c => ResolveNode(c, childDesc)).ToList();
        }

        return node with { ResolvedName = fd.Name, SchemaMismatch = mismatch, Children = children };
    }

    static string? FindResolvedPath(IReadOnlyList<WireNode> tree, string numericPath)
    {
        foreach (var n in tree)
        {
            if (n.Path == numericPath) return n.ResolvedName;
            var deeper = FindResolvedPath(n.Children, numericPath);
            if (deeper is not null)
                return n.ResolvedName is null ? deeper : $"{n.ResolvedName} -> {deeper}";
        }
        return null;
    }
}
