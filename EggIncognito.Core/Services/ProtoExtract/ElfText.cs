namespace EggIncognito.Services.ProtoExtract;

public static class ElfText {
    public static bool TryFindText(byte[] bin, out int fileOff, out int size, out ulong vmAddr) {
        fileOff = 0;
        size = 0;
        vmAddr = 0;
        var s = Elf64.FindSection(bin, ".text");
        if (s is null) return false;
        if (s.FileOffset < 0 || s.Size <= 0 || s.FileOffset + s.Size > bin.Length) return false;
        fileOff = (int)s.FileOffset;
        size = (int)s.Size;
        vmAddr = s.VAddr;
        return true;
    }
}
