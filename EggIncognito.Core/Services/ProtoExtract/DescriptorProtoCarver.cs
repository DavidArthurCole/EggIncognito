using System.Text;
using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace EggIncognito.Services.ProtoExtract;


public static class DescriptorProtoCarver
{
    public sealed record CarvedDescriptor(string Name, int FileOffset, byte[] Bytes);
   
    public sealed record ExtractResult(bool Ok, string? Proto, string Diagnostics, string? ProtoSha,
        IReadOnlyList<string> Messages, string? AppVersion = null, string? Build = null);

   
   
    private static readonly string[] DescriptorFiles = ["ei.proto", "common.proto", "abb.proto"];

   
    public static IReadOnlyList<CarvedDescriptor> CarveAll(byte[] binary)
    {
        var found = new List<CarvedDescriptor>();
        if (binary is null || binary.Length < 16) return found;
        foreach (var name in DescriptorFiles)
        {
            var at = FindAnchor(binary, name);
            if (at < 0) continue;
            var len = WireWalkLength(binary, at);
            if (len <= 0) continue;
            var bytes = new byte[len];
            Array.Copy(binary, at, bytes, 0, len);
            found.Add(new CarvedDescriptor(name, at, bytes));
        }
        return found;
    }

   
    public static string? EmitProto(byte[] fileDescriptorProtoBytes)
    {
        var fdp = TryParse(fileDescriptorProtoBytes);
        if (fdp is null) return null;
        var sb = new StringBuilder();
        EmitFile(fdp, sb);
        return sb.ToString();
    }

   
   
    public static ExtractResult Extract(byte[] binary)
    {
        var carved = CarveAll(binary);
        var ei = carved.FirstOrDefault(c => c.Name == "ei.proto");
        if (ei is null)
            return new ExtractResult(false, null, "no ei.proto descriptor found (not a descriptor-bearing Egg Inc binary?)", null, []);

        var common = carved.FirstOrDefault(c => c.Name == "common.proto");
        var eiText = EmitProto(ei.Bytes);
        if (eiText is null)
            return new ExtractResult(false, null, $"ei.proto descriptor found at 0x{ei.FileOffset:X} but failed to parse", null, []);
        var commonText = common is not null ? EmitProto(common.Bytes) : null;

        string proto = commonText is not null ? ProtoCleanup.Clean(eiText, commonText) : eiText;
        var sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(proto))).ToLowerInvariant();
        var messages = ProtoTextIndex.Names(proto);

        var eiFdp = TryParse(ei.Bytes) ?? new FileDescriptorProto();
        var diag = $"ei.proto @0x{ei.FileOffset:X}: {eiFdp.MessageType.Count} top-level messages, {eiFdp.EnumType.Count} enums"
                 + (common is not null ? "; merged common.proto (aux)" : "; common.proto absent");
        return new ExtractResult(true, proto, diag, sha, messages);
    }

    private static FileDescriptorProto? TryParse(byte[] bytes)
    {
        try { return FileDescriptorProto.Parser.ParseFrom(bytes); }
        catch { return null; }
    }

   

    private static int FindAnchor(byte[] b, string protoName)
    {
        var nb = Encoding.ASCII.GetBytes(protoName);
        var pat = new byte[nb.Length + 2];
        pat[0] = 0x0A;
        pat[1] = (byte)nb.Length;
        Array.Copy(nb, 0, pat, 2, nb.Length);
        return IndexOf(b, pat);
    }

   
   
    internal static int WireWalkLength(byte[] b, int start)
    {
        int pos = start, lastGood = start;
        try
        {
            while (pos < b.Length)
            {
                int p = pos;
                ulong tag = ReadVarint(b, ref p);
                int fieldNum = (int)(tag >> 3);
                int wire = (int)(tag & 7);
                if (fieldNum is < 1 or > 12 || wire != 2) break;
                pos = p;
                ulong len = ReadVarint(b, ref pos);
                if (pos + (long)len > b.Length) break;
                pos += (int)len;
                lastGood = pos;
            }
        }
        catch
        {
           
        }
        return lastGood - start;
    }

    private static int IndexOf(byte[] hay, byte[] needle)
    {
        for (int i = 0; i <= hay.Length - needle.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < needle.Length; j++)
                if (hay[i + j] != needle[j]) { match = false; break; }
            if (match) return i;
        }
        return -1;
    }

    private static ulong ReadVarint(byte[] b, ref int pos)
    {
        int shift = 0;
        ulong result = 0;
        while (true)
        {
            byte by = b[pos++];
            result |= (ulong)(by & 0x7F) << shift;
            if ((by & 0x80) == 0) break;
            shift += 7;
            if (shift > 63) throw new InvalidDataException("varint too long");
        }
        return result;
    }

   

    private static void EmitFile(FileDescriptorProto f, StringBuilder sb)
    {
        sb.Append("syntax = \"").Append(string.IsNullOrEmpty(f.Syntax) ? "proto2" : f.Syntax).Append("\";\n");
        if (!string.IsNullOrEmpty(f.Package)) sb.Append("package ").Append(f.Package).Append(";\n");
        foreach (var dep in f.Dependency) sb.Append("import \"").Append(dep).Append("\";\n");
        sb.Append('\n');
        foreach (var en in f.EnumType) EmitEnum(en, sb, 0);
        foreach (var m in f.MessageType) EmitMessage(m, sb, 0);
    }

    private static void EmitMessage(DescriptorProto m, StringBuilder sb, int indent)
    {
        var pad = new string(' ', indent * 2);
        sb.Append(pad).Append("message ").Append(m.Name).Append(" {\n");
        foreach (var en in m.EnumType) EmitEnum(en, sb, indent + 1);
        foreach (var nested in m.NestedType) EmitMessage(nested, sb, indent + 1);
        var p2 = new string(' ', (indent + 1) * 2);
        foreach (var fld in m.Field)
        {
            string label = fld.Label switch
            {
                FieldDescriptorProto.Types.Label.Required => "required",
                FieldDescriptorProto.Types.Label.Repeated => "repeated",
                _ => "optional",
            };
            string def = fld.HasDefaultValue ? $" [default = {fld.DefaultValue}]" : "";
            sb.Append(p2).Append(label).Append(' ').Append(TypeName(fld)).Append(' ')
              .Append(fld.Name).Append(" = ").Append(fld.Number).Append(def).Append(";\n");
        }
        sb.Append(pad).Append("}\n");
    }

    private static void EmitEnum(EnumDescriptorProto e, StringBuilder sb, int indent)
    {
        var pad = new string(' ', indent * 2);
        sb.Append(pad).Append("enum ").Append(e.Name).Append(" {\n");
        var p2 = new string(' ', (indent + 1) * 2);
        foreach (var v in e.Value)
            sb.Append(p2).Append(v.Name).Append(" = ").Append(v.Number).Append(";\n");
        sb.Append(pad).Append("}\n");
    }

   
   
    private static string TypeName(FieldDescriptorProto f)
    {
        if (f.Type is FieldDescriptorProto.Types.Type.Message or FieldDescriptorProto.Types.Type.Enum)
            return f.TypeName.TrimStart('.');
        return f.Type switch
        {
            FieldDescriptorProto.Types.Type.Double => "double",
            FieldDescriptorProto.Types.Type.Float => "float",
            FieldDescriptorProto.Types.Type.Int64 => "int64",
            FieldDescriptorProto.Types.Type.Uint64 => "uint64",
            FieldDescriptorProto.Types.Type.Int32 => "int32",
            FieldDescriptorProto.Types.Type.Fixed64 => "fixed64",
            FieldDescriptorProto.Types.Type.Fixed32 => "fixed32",
            FieldDescriptorProto.Types.Type.Bool => "bool",
            FieldDescriptorProto.Types.Type.String => "string",
            FieldDescriptorProto.Types.Type.Bytes => "bytes",
            FieldDescriptorProto.Types.Type.Uint32 => "uint32",
            FieldDescriptorProto.Types.Type.Sfixed32 => "sfixed32",
            FieldDescriptorProto.Types.Type.Sfixed64 => "sfixed64",
            FieldDescriptorProto.Types.Type.Sint32 => "sint32",
            FieldDescriptorProto.Types.Type.Sint64 => "sint64",
            _ => "bytes",
        };
    }
}
