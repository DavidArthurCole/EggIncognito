using System.Text;

namespace EggIncognito.Services.ProtoExtract;

public sealed class InitArrayLocator {
    private readonly byte[] _bin;
    private readonly IBinaryImage _img;
    private readonly ulong[] _syms;
    private readonly List<ulong> _inits;

    private InitArrayLocator(byte[] bin, IBinaryImage img) {
        _bin = bin;
        _img = img;
        _syms = [.. img.Symbols.Where(s => s.Value != 0).Select(s => s.Value).Distinct().OrderBy(v => v)];
        _inits = [.. img.GetInitArrayTargets().Where(t => t != 0).Distinct()];
        _inits.Sort();
    }

    public static InitArrayLocator? Create(byte[] bin) {
        var img = BinaryImage.Load(bin);
        return img is null ? null : new InitArrayLocator(bin, img);
    }

    public IReadOnlyList<ulong> Inits => _inits;

    public bool TryLocateByString(string needle, out ulong start, out ulong end) {
        start = 0;
        end = 0;
        for (int i = 0; i < _inits.Count; i++) {
            ulong s = _inits[i];
            if (!ContainsString(s, InitEnd(i), needle)) continue;
            start = s;
            ulong cap = i + 1 < _inits.Count ? _inits[i + 1] : EndOf(s);
            end = FunctionEnd(s, cap);
            return true;
        }

        return false;
    }

    private ulong FunctionEnd(ulong start, ulong cap) {
        var lst = Arm64DataTableReader.ListRange(_bin, start, cap, 200_000);
        if (!lst.Ok) return cap;
        foreach (var insn in lst.Instructions) {
            if (insn.Mnemonic.StartsWith("ret", StringComparison.Ordinal)) return insn.Va + 4;
        }

        return cap;
    }

    private bool ContainsString(ulong startVa, ulong endVa, string needle) {
        var scan = Arm64DataTableReader.ScanRange(_bin, startVa, endVa);
        if (!scan.Ok) return false;
        foreach (var r in scan.Addresses) {
            if (!IsStringSection(r.Section)) continue;
            if (ReadCstr(r.Va).Contains(needle, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    private ulong InitEnd(int i) {
        ulong start = _inits[i];
        ulong next = i + 1 < _inits.Count ? _inits[i + 1] : EndOf(start);
        return next <= start || next - start > 0x6000 ? start + 0x3000 : next;
    }

    private ulong EndOf(ulong start) {
        int lo = 0;
        int hi = _syms.Length;
        while (lo < hi) {
            int mid = (lo + hi) / 2;
            if (_syms[mid] <= start) lo = mid + 1;
            else hi = mid;
        }

        return lo < _syms.Length ? _syms[lo] : start;
    }

    private static bool IsStringSection(string name) =>
        name is "__cstring" or ".rodata" or ".data.rel.ro" or "__const";

    private string ReadCstr(ulong va) {
        if (!_img.TryVaToFileOffset(va, out int fo, out _)) return "";
        int end = fo;
        while (end < _bin.Length && _bin[end] != 0 && end - fo < 200) end++;
        return Encoding.UTF8.GetString(_bin, fo, end - fo);
    }
}
