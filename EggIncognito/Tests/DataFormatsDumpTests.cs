using EggIncognito.Services.Syntax;

namespace EggIncognito.Tests;

public class DataFormatsDumpTests {
    private static byte[] Sample(int n) {
        var bytes = new byte[n];
        for (int i = 0; i < n; i++) bytes[i] = (byte)(i * 7 % 256);
        return bytes;
    }

    [Fact]
    public void HexDump_TextCarriesNoOffsets() {
        var dump = DataFormats.HexDump(Sample(40));
        foreach (string line in dump.Text.Split('\n')) {
            Assert.DoesNotMatch("^[0-9a-f]{8}  ", line);
        }
    }

    [Fact]
    public void HexDump_HasOneLabelPerLine() {
        var dump = DataFormats.HexDump(Sample(40));
        Assert.Equal(dump.Text.Split('\n').Length, dump.Labels.Count);
        Assert.Equal("00000000", dump.Labels[0]);
        Assert.Equal("00000010", dump.Labels[1]);
        Assert.Equal("00000020", dump.Labels[2]);
    }

    [Fact]
    public void BinDump_HasOneLabelPerLine() {
        var dump = DataFormats.BinDump(Sample(20));
        Assert.Equal(dump.Text.Split('\n').Length, dump.Labels.Count);
        Assert.Equal("00000000", dump.Labels[0]);
        Assert.Equal("00000008", dump.Labels[1]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(64)]
    [InlineData(129)]
    public void JoinedHexDump_MatchesTheLegacyLayout(int n) {
        byte[] bytes = Sample(n);
        Assert.Equal(LegacyHex(bytes), DataFormats.ToHexDump(bytes));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(64)]
    public void JoinedBinDump_MatchesTheLegacyLayout(int n) {
        byte[] bytes = Sample(n);
        Assert.Equal(LegacyBin(bytes), DataFormats.ToBinDump(bytes));
    }

    [Fact]
    public void EmptyInput_ReportsEmptyInBothForms() {
        Assert.Equal("(empty)", DataFormats.ToHexDump([]));
        Assert.Equal("(empty)", DataFormats.ToBinDump([]));
        Assert.Equal("(empty)", DataFormats.HexDump([]).Text);
        Assert.Single(DataFormats.HexDump([]).Labels);
    }

    [Fact]
    public void BytesToText_StillJoinsLabelAndLine() {
        Assert.Equal(DataFormats.Join(DataFormats.BytesToDump("AAEC", "hex")),
            DataFormats.BytesToText("AAEC", "hex"));
        Assert.StartsWith("00000000  00 01 02", DataFormats.BytesToText("AAEC", "hex"));
    }

    private static string LegacyHex(byte[] bytes) {
        if (bytes.Length == 0) return "(empty)";
        var lines = new List<string>();
        for (int i = 0; i < bytes.Length; i += 16) {
            byte[] slice = [.. bytes.Skip(i).Take(16)];
            string off = i.ToString("x8");
            string hex = string.Join(" ", slice.Select(b => b.ToString("x2"))).PadRight(16 * 3 - 1, ' ');
            string ascii = string.Concat(slice.Select(b => b is >= 32 and < 127 ? (char)b : '.'));
            lines.Add($"{off}  {hex}  |{ascii}|");
        }

        return string.Join("\n", lines);
    }

    private static string LegacyBin(byte[] bytes) {
        if (bytes.Length == 0) return "(empty)";
        var lines = new List<string>();
        for (int i = 0; i < bytes.Length; i += 8) {
            byte[] slice = [.. bytes.Skip(i).Take(8)];
            string off = i.ToString("x8");
            string bits = string.Join(" ", slice.Select(b => Convert.ToString(b, 2).PadLeft(8, '0')));
            lines.Add($"{off}  {bits}");
        }

        return string.Join("\n", lines);
    }
}
