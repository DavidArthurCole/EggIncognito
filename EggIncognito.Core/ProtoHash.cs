using Ei;
using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace EggIncognito.Core;

public static class ProtoHash {
    public static string Of(string protoText) => Hashes.Sha256Hex(protoText);

    public static string OfDescriptor(byte[] fileDescriptorProtoBytes) =>
        HashCanonical(FileDescriptorProto.Parser.ParseFrom(fileDescriptorProtoBytes));

    public static string OfDescriptor(FileDescriptorProto fdp) => HashCanonical(fdp.Clone());

    public static string Current() => HashCanonical(AuthenticatedMessage.Descriptor.File.ToProto());

    private static string HashCanonical(FileDescriptorProto fdp) {
        Canonicalize(fdp);
        return Hashes.Sha256Hex(fdp.ToByteArray());
    }

    private static void Canonicalize(FileDescriptorProto f) {
        f.Options = null;
        f.SourceCodeInfo = null;
        f.Service.Clear();
        foreach (var m in f.MessageType) CanonMessage(m);
        foreach (var e in f.EnumType) CanonEnum(e);
        foreach (var x in f.Extension) CanonField(x);
    }

    private static void CanonMessage(DescriptorProto m) {
        m.Options = null;
        m.ReservedRange.Clear();
        m.ReservedName.Clear();
        m.ExtensionRange.Clear();
        foreach (var fld in m.Field) CanonField(fld);
        foreach (var x in m.Extension) CanonField(x);
        foreach (var od in m.OneofDecl) od.Options = null;
        foreach (var n in m.NestedType) CanonMessage(n);
        foreach (var e in m.EnumType) CanonEnum(e);
    }

    private static void CanonField(FieldDescriptorProto fld) {
        fld.ClearJsonName();
        fld.Options = null;
    }

    private static void CanonEnum(EnumDescriptorProto e) {
        e.Options = null;
        e.ReservedRange.Clear();
        e.ReservedName.Clear();
        foreach (var v in e.Value) v.Options = null;
    }
}
