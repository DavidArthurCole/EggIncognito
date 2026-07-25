using System.Globalization;
using System.Text;
using Google.Protobuf.Reflection;

namespace EggIncognito.Services;

public static class WireForensics {
    private const int MaxDepth = 64;
    private const int MaxNodes = 200_000;

    private static readonly string[] WireNames = ["varint", "i64", "len", "sgroup", "egroup", "i32", "?6", "?7"];

    private static (ulong Value, int Next) ReadVarint(ReadOnlySpan<byte> buf, int pos) {
        ulong result = 0;
        int shift = 0;
        int start = pos;
        while (pos < buf.Length) {
            byte b = buf[pos++];
            result |= (ulong)(b & 0x7f) << shift;
            if ((b & 0x80) == 0) return (result, pos);
            shift += 7;
            if (shift > 63) throw new WireException("varint too long (>10 bytes)", start);
        }

        throw new WireException("truncated varint (hit end of buffer)", start);
    }


    private static bool LenFits(ulong declaredLen, int pos, int end) =>
        pos <= end && declaredLen <= (ulong)(end - pos);


    private static bool LooksLikeMessage(ReadOnlySpan<byte> buf, int start, int end) {
        if (start == end) return false;
        int pos = start;
        try {
            while (pos < end) {
                (ulong tag, int next) = ReadVarint(buf, pos);
                pos = next;
                int wire = (int)(tag & 0x7);
                int field = (int)(tag >> 3);
                if (field == 0) return false;
                switch (wire) {
                    case 0: pos = ReadVarint(buf, pos).Next; break;
                    case 1: pos += 8; break;
                    case 5: pos += 4; break;
                    case 2:
                        (ulong len, int lnext) = ReadVarint(buf, pos);
                        if (!LenFits(len, lnext, end)) return false;
                        pos = lnext + (int)len;
                        break;
                    default: return false;
                }

                if (pos > end) return false;
            }

            return pos == end;
        } catch {
            return false;
        }
    }

    private static List<WireNode> WalkMessage(ReadOnlySpan<byte> buf, int start, int end, string path, int depth,
        Ctx ctx) {
        var nodes = new List<WireNode>();
        int pos = start;
        while (pos < end) {
            int fieldOffset = pos;
            ulong tag;
            try {
                (tag, pos) = ReadVarint(buf, pos);
            } catch (WireException e) {
                ctx.Record(path, null, e.Offset, e.Message);
                break;
            }

            int wire = (int)(tag & 0x7);
            int field = (int)(tag >> 3);
            string fieldPath = path.Length == 0 ? field.ToString(CultureInfo.InvariantCulture) : $"{path}.{field}";

            if (field == 0) {
                ctx.Record(fieldPath, null, fieldOffset, "field number 0 (illegal)");
                break;
            }

            int? len = null;
            int? dataStart = null, dataEnd = null;
            List<WireNode> children = [];
            try {
                switch (wire) {
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
                        (ulong l, int bodyStart) = ReadVarint(buf, pos);
                        if (l <= int.MaxValue) len = (int)l;
                        if (!LenFits(l, bodyStart, end)) {
                            throw new WireException(
                                $"len-field declares {l} bytes but only {Math.Max(0, end - bodyStart)} remain (overrun)",
                                fieldOffset);
                        }

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
            } catch (WireException e) {
                ctx.Record(fieldPath, null, e.Offset, e.Message);
                nodes.Add(new WireNode(fieldPath, null, field, WireNames[wire], fieldOffset, len, dataStart, dataEnd,
                    false, children));
                break;
            }

            ctx.NodeCount++;
            nodes.Add(new WireNode(fieldPath, null, field, WireNames[wire], fieldOffset, len, dataStart, dataEnd, false,
                children));
            if (ctx.NodeCount >= MaxNodes) break;
        }

        return nodes;
    }

    private static HexWindow BuildHexWindow(ReadOnlySpan<byte> buf, int errorOffset) {
        int lo = Math.Max(0, errorOffset - 16);
        int hi = Math.Min(buf.Length, errorOffset + 16);
        return new HexWindow(lo, hi, errorOffset - lo, Convert.ToHexString(buf[lo..hi]).ToLowerInvariant());
    }


    private static List<SalvagedString> SalvageStrings(ReadOnlySpan<byte> buf, int start, int end, int minLen = 4) {
        var outp = new List<SalvagedString>();
        int runLen = 0;
        int from = Math.Max(0, start), to = Math.Min(buf.Length, end);
        for (int i = from; i <= to; i++) {
            bool printable = i < to && buf[i] >= 0x20 && buf[i] <= 0x7e;
            if (printable) {
                runLen++;
                continue;
            }

            if (runLen >= minLen)
                outp.Add(new SalvagedString(i - runLen, Encoding.Latin1.GetString(buf.Slice(i - runLen, runLen))));
            runLen = 0;
        }

        return outp;
    }

    public static DiagnoseResult Diagnose(ReadOnlySpan<byte> bytes, string? rootTypeName,
        IProtoReflection? reflection) {
        var ctx = new Ctx();
        IReadOnlyList<WireNode> tree = WalkMessage(bytes, 0, bytes.Length, "", 0, ctx);

        var first = ctx.Errors.Count == 0
            ? null
            : ctx.Errors.Aggregate((a, b) => b.Offset < a.Offset ? b : a);

        var hex = first is null ? null : BuildHexWindow(bytes, first.Offset);

        List<SalvagedString> salvaged = [];
        if (first is not null) {
            int lo = Math.Max(0, first.Offset - 64);
            int hi = Math.Min(bytes.Length, first.Offset + 256);
            salvaged = SalvageStrings(bytes, lo, hi);
        }

        MessageDescriptor? rootDesc = null;
        if (rootTypeName is not null && reflection is not null) {
            rootDesc = reflection.FindMessage(rootTypeName);
            tree = ResolveSchema(tree, rootTypeName, reflection);
            if (first is not null) {
                string? resolvedPath = FindResolvedPath(tree, first.Path);
                if (resolvedPath is not null) first = first with { ResolvedPath = resolvedPath };
            }
        }


        Recovery? recovered = null;
        if (first is not null) {
            int recoverStart = EnclosingRegionStart(tree, first.Offset, 0);
            int recoverEnd = Math.Min(bytes.Length, first.Offset + 4096);
            (var fields, int aligned, int skipped) = RecoverFields(bytes, recoverStart, recoverEnd);
            if (fields.Count > 0) {
                var resolved = ResolveRecovered(fields, first.Path, rootDesc);
                recovered = new Recovery(aligned, skipped, resolved);
            }
        }

        return new DiagnoseResult(
            ctx.Errors.Count == 0,
            bytes.Length,
            ctx.NodeCount,
            first,
            ctx.Errors,
            hex,
            salvaged,
            tree,
            recovered);
    }


    private static int EnclosingRegionStart(IReadOnlyList<WireNode> tree, int errorOffset, int fallbackStart) {
        foreach (var n in tree) {
            if (n.DataStart is int ds && n.DataEnd is int de && n.Children.Count > 0
                && errorOffset >= ds && errorOffset < de) {
                return EnclosingRegionStart(n.Children, errorOffset, ds);
            }
        }

        return fallbackStart;
    }

    private static (RecoveredField Node, int Next)? TryField(ReadOnlySpan<byte> buf, int pos, int end) {
        ulong tag;
        try {
            (tag, pos) = ReadVarint(buf, pos);
        } catch {
            return null;
        }

        int wire = (int)(tag & 0x7);
        int field = (int)(tag >> 3);
        if (field is 0 or > 0x1fffffff) return null;

        switch (wire) {
            case 0:
                ulong v;
                try {
                    (v, pos) = ReadVarint(buf, pos);
                } catch {
                    return null;
                }

                return (new RecoveredField(field, null, "varint", v.ToString(CultureInfo.InvariantCulture), false),
                    pos);
            case 1:
                if (pos + 8 > end) return null;
                double d = BitConverter.ToDouble(buf.Slice(pos, 8));
                return (new RecoveredField(field, null, "i64", d.ToString(CultureInfo.InvariantCulture), false),
                    pos + 8);
            case 5:
                if (pos + 4 > end) return null;
                float fl = BitConverter.ToSingle(buf.Slice(pos, 4));
                return (new RecoveredField(field, null, "i32", fl.ToString(CultureInfo.InvariantCulture), false),
                    pos + 4);
            case 2:
                ulong lenRaw;
                try {
                    (lenRaw, pos) = ReadVarint(buf, pos);
                } catch {
                    return null;
                }

                if (!LenFits(lenRaw, pos, end)) return null;
                int len = (int)lenRaw;
                var slice = buf.Slice(pos, len);
                bool printable = len > 0 && IsPrintable(slice);
                var node = printable
                    ? new RecoveredField(field, null, "len", Encoding.UTF8.GetString(slice), false)
                    : new RecoveredField(field, null, "len", $"<{len} bytes>", true);
                return (node, pos + len);
            default:
                return null;
        }
    }

    private static bool IsPrintable(ReadOnlySpan<byte> s) {
        foreach (byte c in s) {
            if (c is not (0x09 or 0x0a or >= 0x20 and <= 0x7e))
                return false;
        }

        return true;
    }


    private static (List<RecoveredField> Fields, int CleanPrefix, int Skipped) RecoverLinear(ReadOnlySpan<byte> buf,
        int from, int end) {
        var fields = new List<RecoveredField>();
        int pos = from, skip = 0, cleanPrefix = 0;
        bool sawSkip = false;
        while (pos < end) {
            var r = TryField(buf, pos, end);
            if (r is { } hit) {
                fields.Add(hit.Node);
                if (!sawSkip) cleanPrefix++;
                pos = hit.Next;
                continue;
            }

            sawSkip = true;
            skip++;
            pos++;
            if (skip > 4096) break;
        }

        return (fields, cleanPrefix, skip);
    }


    private static (IReadOnlyList<RecoveredField> Fields, int AlignedAt, int Skipped) RecoverFields(
        ReadOnlySpan<byte> buf, int start, int end, int maxProbe = 8) {
        (List<RecoveredField> f, int prefix, int skip, int off)? best = null;
        int hi = Math.Min(start + maxProbe, end - 1);
        for (int off = start; off <= hi; off++) {
            (var f, int prefix, int skip) = RecoverLinear(buf, off, end);
            int score = prefix * 1000 - off;
            int bestScore = best is { } b ? b.prefix * 1000 - b.off : int.MinValue;
            if (best is null || score > bestScore) best = (f, prefix, skip, off);
        }

        return best is { } bb ? (bb.f, bb.off, bb.skip) : ([], start, 0);
    }


    private static IReadOnlyList<RecoveredField> ResolveRecovered(IReadOnlyList<RecoveredField> fields,
        string brokenPath, MessageDescriptor? rootDesc) {
        if (rootDesc is null) return fields;

        int[] segs = [.. brokenPath.Split('.').Select(s => int.Parse(s, CultureInfo.InvariantCulture))];
        var desc = rootDesc;
        for (int i = 0; i < segs.Length - 1 && desc is not null; i++) {
            var fd = desc.Fields.InFieldNumberOrder().FirstOrDefault(f => f.FieldNumber == segs[i]);
            desc = fd is { FieldType: FieldType.Message or FieldType.Group } ? fd.MessageType : null;
        }

        if (desc is null) return fields;
        var parent = desc;
        return fields.Select(rf => {
            var fd = parent.Fields.InFieldNumberOrder().FirstOrDefault(f => f.FieldNumber == rf.Field);
            return rf with { ResolvedName = fd?.Name };
        }).ToList();
    }


    private static int ExpectedWire(FieldDescriptor f) {
        return f.IsRepeated && f.IsPacked
            ? 2
            : f.FieldType switch {
                FieldType.Int32 or FieldType.Int64 or FieldType.UInt32 or FieldType.UInt64
                    or FieldType.SInt32 or FieldType.SInt64 or FieldType.Bool or FieldType.Enum => 0,
                FieldType.Fixed64 or FieldType.SFixed64 or FieldType.Double => 1,
                FieldType.Fixed32 or FieldType.SFixed32 or FieldType.Float => 5,
                FieldType.String or FieldType.Bytes or FieldType.Message or FieldType.Group => 2,
                _ => -1
            };
    }

    private static int WireIndex(string wireName) => Array.IndexOf(WireNames, wireName);

    private static IReadOnlyList<WireNode> ResolveSchema(IReadOnlyList<WireNode> tree, string rootTypeName,
        IProtoReflection reflection) {
        var rootDesc = reflection.FindMessage(rootTypeName);
        return rootDesc is null ? tree : tree.Select(n => ResolveNode(n, rootDesc)).ToList();
    }

    private static WireNode ResolveNode(WireNode node, MessageDescriptor desc) {
        var fd = desc.Fields.InFieldNumberOrder().FirstOrDefault(f => f.FieldNumber == node.Field);
        if (fd is null)
            return node with { ResolvedName = "<unknown>" };

        int expected = ExpectedWire(fd);
        int actual = WireIndex(node.Wire);
        bool mismatch = expected >= 0 && actual >= 0 && expected != actual;

        var children = node.Children;
        if (node.Children.Count > 0 && (fd.FieldType == FieldType.Message || fd.FieldType == FieldType.Group)) {
            var childDesc = fd.MessageType;
            children = node.Children.Select(c => ResolveNode(c, childDesc)).ToList();
        }

        return node with { ResolvedName = fd.Name, SchemaMismatch = mismatch, Children = children };
    }

    private static string? FindResolvedPath(IReadOnlyList<WireNode> tree, string numericPath) {
        foreach (var n in tree) {
            if (n.Path == numericPath) return n.ResolvedName;
            string? deeper = FindResolvedPath(n.Children, numericPath);
            if (deeper is not null)
                return n.ResolvedName is null ? deeper : $"{n.ResolvedName} -> {deeper}";
        }

        return null;
    }

    public sealed record WireError(int Offset, string Path, string? ResolvedPath, string Message);

    public sealed record HexWindow(int From, int To, int ErrorIndexInWindow, string Hex);

    public sealed record SalvagedString(int Offset, string Text);


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


    public sealed record RecoveredField(int Field, string? ResolvedName, string Wire, string Value, bool Bad);


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

    private sealed class Ctx {
        public readonly List<WireError> Errors = [];
        public int NodeCount;

        public void Record(string path, string? resolved, int offset, string message) =>
            Errors.Add(new WireError(offset, path, resolved, message));
    }

    private sealed class WireException(string message, int offset) : Exception(message) {
        public int Offset { get; } = offset;
    }
}
