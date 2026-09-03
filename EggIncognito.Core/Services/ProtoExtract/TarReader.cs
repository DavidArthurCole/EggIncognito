using System.Text;

namespace EggIncognito.Core.Services.ProtoExtract;

public static class TarReader {
    private const int BlockSize = 512;

    public static IReadOnlyList<(string Name, byte[] Bytes)> Read(byte[] tar) {
        var entries = new List<(string, byte[])>();
        if (tar is null || tar.Length < BlockSize) return entries;

        int pos = 0;
        string? longName = null;
        while (pos + BlockSize <= tar.Length) {
            if (IsZeroBlock(tar, pos)) break;

            long size = ParseOctal(tar, pos + 124, 12);
            char typeFlag = (char)tar[pos + 156];
            string name = longName ?? ParseName(tar, pos);
            longName = null;

            int dataStart = pos + BlockSize;
            if (size < 0 || dataStart + size > tar.Length) break;

            switch (typeFlag) {
                case 'L':
                    longName = Encoding.UTF8.GetString(tar, dataStart, (int)size).TrimEnd('\0');
                    break;
                case '0':
                case '\0':
                    byte[] bytes = new byte[size];
                    Array.Copy(tar, dataStart, bytes, 0, (int)size);
                    entries.Add((name, bytes));
                    break;
            }

            long dataBlocks = (size + BlockSize - 1) / BlockSize;
            pos = dataStart + (int)(dataBlocks * BlockSize);
        }

        return entries;
    }

    private static string ParseName(byte[] tar, int header) {
        string name = ReadString(tar, header, 100);
        string prefix = ReadString(tar, header + 345, 155);
        return prefix.Length > 0 ? prefix + "/" + name : name;
    }

    private static string ReadString(byte[] buf, int offset, int max) {
        int end = offset;
        int limit = Math.Min(offset + max, buf.Length);
        while (end < limit && buf[end] != 0) end++;
        return Encoding.UTF8.GetString(buf, offset, end - offset);
    }

    private static long ParseOctal(byte[] buf, int offset, int len) {
        long value = 0;
        int limit = Math.Min(offset + len, buf.Length);
        for (int i = offset; i < limit; i++) {
            byte c = buf[i];
            if (c is 0 or (byte)' ') {
                if (value == 0 && c == 0) continue;
                break;
            }

            if (c is < (byte)'0' or > (byte)'7') return -1;
            value = (value << 3) + (c - '0');
        }

        return value;
    }

    private static bool IsZeroBlock(byte[] buf, int offset) {
        for (int i = offset; i < offset + BlockSize; i++) {
            if (buf[i] != 0)
                return false;
        }

        return true;
    }
}
