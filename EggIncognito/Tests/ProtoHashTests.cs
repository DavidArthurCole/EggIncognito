using System.Text.RegularExpressions;
using EggIncognito.Core;
using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace EggIncognito.Tests;

public partial class ProtoHashTests {
    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex HexSha();

    [Fact]
    public void Current_Is_Deterministic_HexSha() {
        string a = ProtoHash.Current();
        string b = ProtoHash.Current();
        Assert.Equal(a, b);
        Assert.Matches(HexSha(), a);
    }

    [Fact]
    public void OfDescriptor_Ignores_JsonName_And_Options() {
        var bare = new FileDescriptorProto {
            Name = "x.proto",
            Syntax = "proto2",
            MessageType = { new DescriptorProto {
                Name = "M",
                Field = { new FieldDescriptorProto {
                    Name = "a", Number = 1,
                    Label = FieldDescriptorProto.Types.Label.Optional,
                    Type = FieldDescriptorProto.Types.Type.Int32
                } }
            } }
        };

        var noisy = bare.Clone();
        noisy.Options = new Google.Protobuf.Reflection.FileOptions { Deprecated = true };
        noisy.MessageType[0].Field[0].JsonName = "aJson";
        noisy.MessageType[0].Field[0].Options = new FieldOptions { Deprecated = true };

        Assert.Equal(ProtoHash.OfDescriptor(bare.ToByteArray()), ProtoHash.OfDescriptor(noisy.ToByteArray()));
    }

    [Fact]
    public void OfDescriptor_Distinguishes_Different_Schemas() {
        var one = new FileDescriptorProto {
            Name = "x.proto",
            Syntax = "proto2",
            MessageType = { new DescriptorProto { Name = "M" } }
        };
        var two = one.Clone();
        two.MessageType[0].Field.Add(new FieldDescriptorProto {
            Name = "a",
            Number = 1,
            Label = FieldDescriptorProto.Types.Label.Optional,
            Type = FieldDescriptorProto.Types.Type.Int32
        });

        Assert.NotEqual(ProtoHash.OfDescriptor(one.ToByteArray()), ProtoHash.OfDescriptor(two.ToByteArray()));
    }
}
