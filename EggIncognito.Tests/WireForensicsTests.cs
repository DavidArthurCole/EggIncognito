using System;
using System.Linq;
using EggIncognito.Services;
using Xunit;

namespace EggIncognito.Tests;

public class WireForensicsTests
{
    static byte[] Bytes(params int[] b) => b.Select(x => (byte)x).ToArray();

    [Fact]
    public void CleanMessage_OkTrue_NoErrors()
    {
        // field 1 varint=1, field 2 varint=300 (0xAC 0x02)
        var bytes = Bytes(0x08, 0x01, 0x10, 0xAC, 0x02);
        var r = WireForensics.Diagnose(bytes, null, null);
        Assert.True(r.Ok);
        Assert.Null(r.FirstError);
        Assert.Equal(bytes.Length, r.TotalLen);
    }

    [Fact]
    public void TruncatedVarint_ReportsOffsetAtTag()
    {
        // field 1 varint with continuation bit set but buffer ends => truncated
        var bytes = Bytes(0x08, 0x80); // 0x80 has continuation bit, no following byte
        var r = WireForensics.Diagnose(bytes, null, null);
        Assert.False(r.Ok);
        Assert.NotNull(r.FirstError);
        Assert.Equal(1, r.FirstError!.Offset); // the value varint starts at offset 1
    }

    [Fact]
    public void LenOverrun_ReportsOffsetAtField()
    {
        // field 1 LEN, declares 10 bytes but only 1 follows
        var bytes = Bytes(0x0A, 0x0A, 0x41); // tag@0, len=10@1, only 'A'@2
        var r = WireForensics.Diagnose(bytes, null, null);
        Assert.False(r.Ok);
        Assert.NotNull(r.FirstError);
        Assert.Equal(0, r.FirstError!.Offset); // error attributed to the field tag
        Assert.Contains("overrun", r.FirstError.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IllegalWireType_Reports()
    {
        // tag with wire type 7 (illegal): field 1 wire 7 => (1<<3)|7 = 0x0F
        var bytes = Bytes(0x0F, 0x00);
        var r = WireForensics.Diagnose(bytes, null, null);
        Assert.False(r.Ok);
        Assert.NotNull(r.FirstError);
        Assert.Equal(0, r.FirstError!.Offset);
    }

    [Fact]
    public void FieldNumberZero_Illegal()
    {
        // tag 0x00 => field 0, wire 0
        var bytes = Bytes(0x00, 0x01);
        var r = WireForensics.Diagnose(bytes, null, null);
        Assert.False(r.Ok);
        Assert.NotNull(r.FirstError);
    }

    [Fact]
    public void NestedMessage_Descends()
    {
        // field 1 LEN containing { field 1 varint = 1 }: 0x0A 0x02 0x08 0x01
        var bytes = Bytes(0x0A, 0x02, 0x08, 0x01);
        var r = WireForensics.Diagnose(bytes, null, null);
        Assert.True(r.Ok);
        var top = Assert.Single(r.Tree);
        Assert.NotEmpty(top.Children); // descended into the nested message
        Assert.Equal("1.1", top.Children[0].Path);
    }

    [Fact]
    public void HexWindow_MarksErrorByte()
    {
        var bytes = Bytes(0x08, 0x80);
        var r = WireForensics.Diagnose(bytes, null, null);
        Assert.NotNull(r.HexAround);
        Assert.True(r.HexAround!.ErrorIndexInWindow >= 0);
    }

    [Fact]
    public void Salvage_RecoversAsciiRunInBrokenSpan()
    {
        // field 1 LEN declares 99 bytes (overrun) followed by readable ascii so the salvage window catches it.
        var prefix = Bytes(0x0A, 0x63); // tag field1 LEN, len=99 (overruns)
        var ascii = System.Text.Encoding.ASCII.GetBytes("corona-virus");
        var bytes = prefix.Concat(ascii).ToArray();
        var r = WireForensics.Diagnose(bytes, null, null);
        Assert.False(r.Ok);
        Assert.Contains(r.Salvaged, s => s.Text.Contains("corona-virus"));
    }

    // Schema layer: AuthenticatedMessage field 3 = version (uint32/varint), field 4 = compressed (bool/varint).
    [Fact]
    public void Schema_ResolvesFieldNames()
    {
        // field 3 (version, varint): tag (3<<3)|0 = 0x18, value 1
        var bytes = Bytes(0x18, 0x01);
        var r = WireForensics.Diagnose(bytes, "AuthenticatedMessage", new ProtoReflection());
        var node = Assert.Single(r.Tree);
        Assert.Equal("version", node.ResolvedName);
        Assert.False(node.SchemaMismatch); // varint field carrying a varint => correct
    }

    [Fact]
    public void Schema_FlagsWireMismatch()
    {
        // Put a LEN payload where field 3 (version, expects varint) lives:
        // tag (3<<3)|2 = 0x1A, len 1, byte 'A'
        var bytes = Bytes(0x1A, 0x01, 0x41);
        var r = WireForensics.Diagnose(bytes, "AuthenticatedMessage", new ProtoReflection());
        var node = Assert.Single(r.Tree);
        Assert.Equal("version", node.ResolvedName);
        Assert.True(node.SchemaMismatch); // varint field carrying a LEN blob => corruption signature
    }

    [Fact]
    public void Recovery_RecoversFieldsPastCorruption()
    {
        // A corrupt LEN field (declares 99 bytes, overruns) followed by intact fields:
        //   field 1 LEN len=99 (corrupt/overrun)  => 0x0A 0x63
        //   field 2 LEN "hello"                    => 0x12 0x05 h e l l o
        //   field 3 varint = 7                      => 0x18 0x07
        var ascii = System.Text.Encoding.ASCII.GetBytes("hello");
        var bytes = Bytes(0x0A, 0x63)
            .Concat(Bytes(0x12, 0x05)).Concat(ascii)
            .Concat(Bytes(0x18, 0x07)).ToArray();
        var r = WireForensics.Diagnose(bytes, null, null);
        Assert.False(r.Ok);
        Assert.NotNull(r.Recovered);
        // The string + the trailing varint should be recovered despite the leading corrupt field.
        Assert.Contains(r.Recovered!.Fields, f => f.Value.Contains("hello"));
        Assert.Contains(r.Recovered.Fields, f => f.Field == 3 && f.Value == "7");
    }

    [Fact]
    public void Recovery_NullOnCleanMessage()
    {
        var bytes = Bytes(0x08, 0x01, 0x10, 0xAC, 0x02);
        var r = WireForensics.Diagnose(bytes, null, null);
        Assert.True(r.Ok);
        Assert.Null(r.Recovered); // no corruption => nothing to recover
    }

    [Fact]
    public void OversizedLenVarint_WalkReportsOverrun()
    {
        // field 1 LEN declaring 2^31 bytes (0x80 0x80 0x80 0x80 0x08).
        var bytes = Bytes(0x0A, 0x80, 0x80, 0x80, 0x80, 0x08);
        var r = WireForensics.Diagnose(bytes, null, null);
        Assert.False(r.Ok);
        Assert.NotNull(r.FirstError);
        Assert.Equal(0, r.FirstError!.Offset);
        Assert.Contains("overrun", r.FirstError.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OversizedLenVarint_NestedProbe_DoesNotLoop()
    {
        // Outer field 1 LEN len=6 whose payload looks like a nested field 1 LEN declaring 0xFFFFFFFA.
        // Must terminate and treat the payload as a leaf, not loop.
        var bytes = Bytes(0x0A, 0x06, 0x0A, 0xFA, 0xFF, 0xFF, 0xFF, 0x0F);
        var r = WireForensics.Diagnose(bytes, null, null);
        Assert.True(r.Ok);
        var top = Assert.Single(r.Tree);
        Assert.Empty(top.Children); // payload rejected as nested message, kept as leaf bytes
    }

    [Fact]
    public void WireNode_LenField_ExposesPayloadRange()
    {
        // field 1 varint=5, field 2 LEN len=4 at offset 2: payload occupies [4, 8).
        var bytes = Bytes(0x08, 0x05, 0x12, 0x04, 0x08, 0x01, 0x10, 0x02);
        var r = WireForensics.Diagnose(bytes, null, null);
        Assert.True(r.Ok);
        var lenNode = r.Tree.Single(n => n.Wire == "len");
        Assert.Equal(4, lenNode.DataStart);
        Assert.Equal(8, lenNode.DataEnd);
        Assert.Equal(2, lenNode.Children.Count);
    }

    [Fact]
    public void EnclosingRegion_ErrorAtNestedBodyEnd_RecoversFromParent()
    {
        // field 1 varint=5, field 2 LEN nested { f1=1, f2=2 } with body [4, 8), then an illegal
        // wire-7 tag exactly at offset 8 = the nested body's END. That error belongs to the parent's
        // next field, not the nested record, so recovery must not realign inside the nested message.
        var bytes = Bytes(0x08, 0x05, 0x12, 0x04, 0x08, 0x01, 0x10, 0x02, 0x0F);
        var r = WireForensics.Diagnose(bytes, null, null);
        Assert.False(r.Ok);
        Assert.Equal(8, r.FirstError!.Offset);
        Assert.NotNull(r.Recovered);
        Assert.Equal(0, r.Recovered!.AlignedAt);
        Assert.Contains(r.Recovered.Fields, f => f.Field == 1 && f.Value == "5");
    }

    [Fact]
    public void OversizedLenVarint_RecoveryResyncs()
    {
        // Corrupt lead field (LEN len=99, overruns) triggers recovery over bytes containing a tag whose
        // LEN declares int.MaxValue. Recovery must resync past it to the intact field 3.
        var bytes = Bytes(0x0A, 0x63)
            .Concat(Bytes(0x12, 0xFF, 0xFF, 0xFF, 0xFF, 0x07))
            .Concat(Bytes(0x18, 0x07)).ToArray();
        var r = WireForensics.Diagnose(bytes, null, null);
        Assert.False(r.Ok);
        Assert.NotNull(r.Recovered);
        Assert.Contains(r.Recovered!.Fields, f => f.Field == 3 && f.Value == "7");
    }
}
