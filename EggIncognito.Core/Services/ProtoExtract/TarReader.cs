using System.Text;

namespace EggIncognito.Services.ProtoExtract;

// Minimal read-only POSIX ustar parser. The iOS asset puller tars a directory of .rpo/.rpoz files on the
// device and scps one tarball back; this walks that tarball into (name, bytes) entries without a third-
// party dependency or shelling to tar host-side. Handles the regular-file + directory typeflags and the
// GNU/ustar long-name cases the BSD tar on a jailbroken iOS emits. Defensive: malformed input yields the
// entries parsed so far, never a throw.
public static class TarReader
{
    private const int BlockSize = 512;

    public static IReadOnlyList<(string Name, byte[] Bytes)> Read(byte[] tar)
    {
        var entries = new List<(string, byte[])>();
        if (tar is null || tar.Length < BlockSize) return entries;

        var pos = 0;
        string? longName = null; // pending name from a GNU 'L' long-name header
        while (pos + BlockSize <= tar.Length)
        {
            // Two consecutive all-zero blocks mark end of archive.
            if (IsZeroBlock(tar, pos)) break;

            var size = ParseOctal(tar, pos + 124, 12);
            var typeFlag = (char)tar[pos + 156];
            var name = longName ?? ParseName(tar, pos);
            longName = null;

            var dataStart = pos + BlockSize;
            if (size < 0 || dataStart + size > tar.Length) break; // truncated/garbage; stop cleanly

            switch (typeFlag)
            {
                case 'L': // GNU long name: this block's data IS the next entry's full name
                    longName = Encoding.UTF8.GetString(tar, dataStart, (int)size).TrimEnd('\0');
                    break;
                case '0':
                case '\0': // regular file (old tar uses NUL typeflag)
                    var bytes = new byte[size];
                    Array.Copy(tar, dataStart, bytes, 0, (int)size);
                    entries.Add((name, bytes));
                    break;
                // directories ('5') and other typeflags: skip the (zero-length) data
            }

            // Advance past the header + data, rounded up to the next 512 boundary.
            var dataBlocks = (size + BlockSize - 1) / BlockSize;
            pos = dataStart + (int)(dataBlocks * BlockSize);
        }
        return entries;
    }

    // ustar name field (100 bytes) optionally prefixed by the 'prefix' field (155 bytes at offset 345).
    private static string ParseName(byte[] tar, int header)
    {
        var name = ReadString(tar, header, 100);
        var prefix = ReadString(tar, header + 345, 155);
        return prefix.Length > 0 ? prefix + "/" + name : name;
    }

    private static string ReadString(byte[] buf, int offset, int max)
    {
        var end = offset;
        var limit = Math.Min(offset + max, buf.Length);
        while (end < limit && buf[end] != 0) end++;
        return Encoding.UTF8.GetString(buf, offset, end - offset);
    }

    // tar sizes are NUL/space-terminated octal ASCII. Returns -1 on garbage.
    private static long ParseOctal(byte[] buf, int offset, int len)
    {
        long value = 0;
        var limit = Math.Min(offset + len, buf.Length);
        for (var i = offset; i < limit; i++)
        {
            var c = buf[i];
            if (c is 0 or (byte)' ') { if (value == 0 && c == 0) continue; else break; }
            if (c < '0' || c > '7') return -1;
            value = (value << 3) + (c - '0');
        }
        return value;
    }

    private static bool IsZeroBlock(byte[] buf, int offset)
    {
        for (var i = offset; i < offset + BlockSize; i++)
            if (buf[i] != 0) return false;
        return true;
    }
}
