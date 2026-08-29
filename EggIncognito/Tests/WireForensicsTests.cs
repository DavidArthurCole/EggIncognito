using System.Text;
using EggIncognito.Core.Services;

namespace EggIncognito.Tests;

public class WireForensicsTests {
    private static byte[] Bytes(params int[] b) => [.. b.Select(x => (byte)x)];

    [Fact]
    public void CleanMessage_OkTrue_NoErrors() {
        byte[] bytes = Bytes(0x08, 0x01, 0x10, 0xAC, 0x02);
        var r = WireForensics.Diagnose(bytes, null, null);
        Assert.True(r.Ok);
        Assert.Null(r.FirstError);
        Assert.Equal(bytes.Length, r.TotalLen);
    }

    [Fact]
    public void TruncatedVarint_ReportsOffsetAtTag() {
        byte[] bytes = Bytes(0x08, 0x80);
        var r = WireForensics.Diagnose(bytes, null, null);
        Assert.False(r.Ok);
        Assert.NotNull(r.FirstError);
        Assert.Equal(1, r.FirstError!.Offset);
    }

    [Fact]
    public void LenOverrun_ReportsOffsetAtField() {
        byte[] bytes = Bytes(0x0A, 0x0A, 0x41);
        var r = WireForensics.Diagnose(bytes, null, null);
        Assert.False(r.Ok);
        Assert.NotNull(r.FirstError);
        Assert.Equal(0, r.FirstError!.Offset);
        Assert.Contains("overrun", r.FirstError.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IllegalWireType_Reports() {
        byte[] bytes = Bytes(0x0F, 0x00);
        var r = WireForensics.Diagnose(bytes, null, null);
        Assert.False(r.Ok);
        Assert.NotNull(r.FirstError);
        Assert.Equal(0, r.FirstError!.Offset);
    }

    [Fact]
    public void FieldNumberZero_Illegal() {
        byte[] bytes = Bytes(0x00, 0x01);
        var r = WireForensics.Diagnose(bytes, null, null);
        Assert.False(r.Ok);
        Assert.NotNull(r.FirstError);
    }

    [Fact]
    public void NestedMessage_Descends() {
        byte[] bytes = Bytes(0x0A, 0x02, 0x08, 0x01);
        var r = WireForensics.Diagnose(bytes, null, null);
        Assert.True(r.Ok);
        var top = Assert.Single(r.Tree);
        Assert.NotEmpty(top.Children);
        Assert.Equal("1.1", top.Children[0].Path);
    }

    [Fact]
    public void HexWindow_MarksErrorByte() {
        byte[] bytes = Bytes(0x08, 0x80);
        var r = WireForensics.Diagnose(bytes, null, null);
        Assert.NotNull(r.HexAround);
        Assert.True(r.HexAround!.ErrorIndexInWindow >= 0);
    }

    [Fact]
    public void Salvage_RecoversAsciiRunInBrokenSpan() {
        byte[] prefix = Bytes(0x0A, 0x63);
        byte[] ascii = Encoding.ASCII.GetBytes("corona-virus");
        byte[] bytes = [.. prefix, .. ascii];
        var r = WireForensics.Diagnose(bytes, null, null);
        Assert.False(r.Ok);
        Assert.Contains(r.Salvaged, s => s.Text.Contains("corona-virus"));
    }


    [Fact]
    public void Schema_ResolvesFieldNames() {
        byte[] bytes = Bytes(0x18, 0x01);
        var r = WireForensics.Diagnose(bytes, "AuthenticatedMessage", new ProtoReflection());
        var node = Assert.Single(r.Tree);
        Assert.Equal("version", node.ResolvedName);
        Assert.False(node.SchemaMismatch);
    }

    [Fact]
    public void Schema_FlagsWireMismatch() {
        byte[] bytes = Bytes(0x1A, 0x01, 0x41);
        var r = WireForensics.Diagnose(bytes, "AuthenticatedMessage", new ProtoReflection());
        var node = Assert.Single(r.Tree);
        Assert.Equal("version", node.ResolvedName);
        Assert.True(node.SchemaMismatch);
    }

    [Fact]
    public void Recovery_RecoversFieldsPastCorruption() {
        byte[] ascii = Encoding.ASCII.GetBytes("hello");
        byte[] bytes = [.. Bytes(0x0A, 0x63), .. Bytes(0x12, 0x05), .. ascii, .. Bytes(0x18, 0x07)];
        var r = WireForensics.Diagnose(bytes, null, null);
        Assert.False(r.Ok);
        Assert.NotNull(r.Recovered);

        Assert.Contains(r.Recovered!.Fields, f => f.Value.Contains("hello"));
        Assert.Contains(r.Recovered.Fields, f => f.Field == 3 && f.Value == "7");
    }

    [Fact]
    public void Recovery_NullOnCleanMessage() {
        byte[] bytes = Bytes(0x08, 0x01, 0x10, 0xAC, 0x02);
        var r = WireForensics.Diagnose(bytes, null, null);
        Assert.True(r.Ok);
        Assert.Null(r.Recovered);
    }

    [Fact]
    public void OversizedLenVarint_WalkReportsOverrun() {
        byte[] bytes = Bytes(0x0A, 0x80, 0x80, 0x80, 0x80, 0x08);
        var r = WireForensics.Diagnose(bytes, null, null);
        Assert.False(r.Ok);
        Assert.NotNull(r.FirstError);
        Assert.Equal(0, r.FirstError!.Offset);
        Assert.Contains("overrun", r.FirstError.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OversizedLenVarint_NestedProbe_DoesNotLoop() {
        byte[] bytes = Bytes(0x0A, 0x06, 0x0A, 0xFA, 0xFF, 0xFF, 0xFF, 0x0F);
        var r = WireForensics.Diagnose(bytes, null, null);
        Assert.True(r.Ok);
        var top = Assert.Single(r.Tree);
        Assert.Empty(top.Children);
    }

    [Fact]
    public void WireNode_LenField_ExposesPayloadRange() {
        byte[] bytes = Bytes(0x08, 0x05, 0x12, 0x04, 0x08, 0x01, 0x10, 0x02);
        var r = WireForensics.Diagnose(bytes, null, null);
        Assert.True(r.Ok);
        var lenNode = r.Tree.Single(n => n.Wire == "len");
        Assert.Equal(4, lenNode.DataStart);
        Assert.Equal(8, lenNode.DataEnd);
        Assert.Equal(2, lenNode.Children.Count);
    }

    [Fact]
    public void EnclosingRegion_ErrorAtNestedBodyEnd_RecoversFromParent() {
        byte[] bytes = Bytes(0x08, 0x05, 0x12, 0x04, 0x08, 0x01, 0x10, 0x02, 0x0F);
        var r = WireForensics.Diagnose(bytes, null, null);
        Assert.False(r.Ok);
        Assert.Equal(8, r.FirstError!.Offset);
        Assert.NotNull(r.Recovered);
        Assert.Equal(0, r.Recovered!.AlignedAt);
        Assert.Contains(r.Recovered.Fields, f => f.Field == 1 && f.Value == "5");
    }

    [Fact]
    public void OversizedLenVarint_RecoveryResyncs() {
        byte[] bytes = [.. Bytes(0x0A, 0x63), .. Bytes(0x12, 0xFF, 0xFF, 0xFF, 0xFF, 0x07), .. Bytes(0x18, 0x07)];
        var r = WireForensics.Diagnose(bytes, null, null);
        Assert.False(r.Ok);
        Assert.NotNull(r.Recovered);
        Assert.Contains(r.Recovered!.Fields, f => f.Field == 3 && f.Value == "7");
    }
}
