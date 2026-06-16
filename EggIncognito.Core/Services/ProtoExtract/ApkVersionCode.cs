using System.IO.Compression;
using System.Text;

namespace EggIncognito.Services.ProtoExtract;

// Reads android:versionCode from an APK without aapt. Opens the zip, finds AndroidManifest.xml (binary
// AXML), and walks the chunked binary-XML format: header, string-pool chunk, optional resource-map
// chunk, then XML node chunks. The START_ELEMENT for `manifest` carries attribute records (ns, name,
// rawValue, typedValue size/type, typedValue data); versionCode is the attribute whose name string is
// "versionCode" with an INT typed value. Returns null (never a fabricated build) when not found or the
// bytes don't parse. The real end-to-end parse is verified live against the device dumpsys
// versionCode; the unit tests here cover the null/garbage path plus a real AndroidManifest.xml lifted from the arm split.
public static class ApkVersionCode
{
    // AXML chunk type tags.
    private const ushort ResXmlType = 0x0003;
    private const ushort ResStringPoolType = 0x0001;
    private const ushort ResXmlStartElementType = 0x0102;
    // Resource value type for an integer.
    private const byte TypeIntDec = 0x10;

    public static string? Read(byte[] apkZipBytes)
    {
        if (apkZipBytes is null || apkZipBytes.Length == 0) return null;
        try
        {
            using var ms = new MemoryStream(apkZipBytes, writable: false);
            using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
            var entry = zip.GetEntry("AndroidManifest.xml");
            if (entry is null) return null;
            using var es = entry.Open();
            using var buf = new MemoryStream();
            es.CopyTo(buf);
            return ParseAxml(buf.ToArray());
        }
        catch
        {
            return null;
        }
    }

    // Parses raw AXML bytes for the manifest element's versionCode int. Defensive: any structural
    // surprise returns null rather than throwing.
    internal static string? ParseAxml(byte[] data)
    {
        try
        {
            if (data.Length < 8) return null;
            var fileType = ReadU16(data, 0);
            if (fileType != ResXmlType) return null;

            var pos = 8; // skip the file header (type, headerSize, fileSize).
            string[]? strings = null;

            while (pos + 8 <= data.Length)
            {
                var type = ReadU16(data, pos);
                var headerSize = ReadU16(data, pos + 2);
                var size = (int)ReadU32(data, pos + 4);
                if (size < 8 || pos + size > data.Length) break;

                if (type == ResStringPoolType)
                {
                    strings = ReadStringPool(data, pos);
                }
                else if (type == ResXmlStartElementType && strings is not null)
                {
                    var code = ReadStartElementVersionCode(data, pos, headerSize, strings);
                    if (code is not null) return code;
                }

                pos += size;
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    // Reads android:versionName (a string) from the manifest. Same AXML walk as the versionCode reader,
    // but the value is the attribute's rawValue string-pool index. Returns null when absent.
    public static string? ReadVersionName(byte[] data)
    {
        try
        {
            if (data.Length < 8 || ReadU16(data, 0) != ResXmlType) return null;
            var pos = 8;
            string[]? strings = null;
            while (pos + 8 <= data.Length)
            {
                var type = ReadU16(data, pos);
                var headerSize = ReadU16(data, pos + 2);
                var size = (int)ReadU32(data, pos + 4);
                if (size < 8 || pos + size > data.Length) break;
                if (type == ResStringPoolType) strings = ReadStringPool(data, pos);
                else if (type == ResXmlStartElementType && strings is not null)
                {
                    var name = ReadStartElementStringAttr(data, pos, headerSize, strings, "versionName");
                    if (name is not null) return name;
                }
                pos += size;
            }
            return null;
        }
        catch { return null; }
    }

    // Returns the rawValue string of the named attribute in a START_ELEMENT chunk, or null.
    private static string? ReadStartElementStringAttr(byte[] data, int chunkPos, int headerSize, string[] strings, string attr)
    {
        var ext = chunkPos + headerSize;
        if (ext + 20 > data.Length) return null;
        var attrStart = ReadU16(data, ext + 8);
        var attrCount = ReadU16(data, ext + 12);
        var baseAttr = ext + attrStart;
        const int attrRecordSize = 20;
        for (var a = 0; a < attrCount; a++)
        {
            var rec = baseAttr + a * attrRecordSize;
            if (rec + attrRecordSize > data.Length) break;
            var nameIdx = (int)ReadU32(data, rec + 4);
            var rawValueIdx = (int)ReadU32(data, rec + 8);
            var name = nameIdx >= 0 && nameIdx < strings.Length ? strings[nameIdx] : null;
            if (name == attr && rawValueIdx >= 0 && rawValueIdx < strings.Length)
                return strings[rawValueIdx];
        }
        return null;
    }

    // Reads versionCode out of one START_ELEMENT chunk if its attributes carry it. Chunk layout after
    // the generic chunk header: lineNumber(4), comment(4), then the start-element ext: ns(4), name(4),
    // attributeStart(2), attributeSize(2), attributeCount(2), idIndex(2), classIndex(2), styleIndex(2),
    // followed by attributeCount records of 5 uint32 each: ns, name, rawValue, typedValue, data. The
    // typedValue field packs size(2)+res0(1)+dataType(1).
    private static string? ReadStartElementVersionCode(byte[] data, int chunkPos, int headerSize, string[] strings)
    {
        var ext = chunkPos + headerSize;
        if (ext + 20 > data.Length) return null;
        var attrStart = ReadU16(data, ext + 8);
        var attrCount = ReadU16(data, ext + 12);

        var baseAttr = ext + attrStart;
        const int attrRecordSize = 20;
        for (var a = 0; a < attrCount; a++)
        {
            var rec = baseAttr + a * attrRecordSize;
            if (rec + attrRecordSize > data.Length) break;
            var nameIdx = (int)ReadU32(data, rec + 4);
            var typedValue = ReadU32(data, rec + 12);
            var dataType = (byte)((typedValue >> 24) & 0xFF);
            var dataVal = ReadU32(data, rec + 16);

            var name = nameIdx >= 0 && nameIdx < strings.Length ? strings[nameIdx] : null;
            if (name == "versionCode" && dataType == TypeIntDec)
                return dataVal.ToString();
        }
        return null;
    }

    // Reads a ResStringPool chunk into a string array. Supports UTF-8 and UTF-16 flavors. Only the
    // fields we need are parsed; styles are ignored.
    private static string[] ReadStringPool(byte[] data, int chunkPos)
    {
        var stringCount = (int)ReadU32(data, chunkPos + 8);
        var flags = ReadU32(data, chunkPos + 16);
        var stringsStart = (int)ReadU32(data, chunkPos + 20);
        var isUtf8 = (flags & 0x100) != 0;

        var result = new string[stringCount];
        var offsetsBase = chunkPos + 28; // after the string-pool header (28 bytes).
        var dataBase = chunkPos + stringsStart;

        for (var i = 0; i < stringCount; i++)
        {
            var off = (int)ReadU32(data, offsetsBase + i * 4);
            var strPos = dataBase + off;
            result[i] = isUtf8 ? ReadUtf8String(data, strPos) : ReadUtf16String(data, strPos);
        }
        return result;
    }

    // UTF-8 strings: a (possibly two-pass) length prefix for the char count, then a length for the byte
    // count, then the bytes. We only need the byte length to slice the string.
    private static string ReadUtf8String(byte[] data, int pos)
    {
        var p = pos;
        p = SkipUtf8Len(data, p); // character count
        var (byteLen, next) = ReadUtf8Len(data, p);
        return Encoding.UTF8.GetString(data, next, byteLen);
    }

    private static int SkipUtf8Len(byte[] data, int pos)
    {
        var (_, next) = ReadUtf8Len(data, pos);
        return next;
    }

    private static (int Len, int Next) ReadUtf8Len(byte[] data, int pos)
    {
        int b = data[pos];
        if ((b & 0x80) != 0)
            return (((b & 0x7F) << 8) | data[pos + 1], pos + 2);
        return (b, pos + 1);
    }

    // UTF-16 strings: a length prefix in 16-bit units (high-bit extension for long strings), then the
    // UTF-16LE code units, then a null terminator.
    private static string ReadUtf16String(byte[] data, int pos)
    {
        int len = ReadU16(data, pos);
        var p = pos + 2;
        if ((len & 0x8000) != 0)
        {
            len = ((len & 0x7FFF) << 16) | ReadU16(data, p);
            p += 2;
        }
        return Encoding.Unicode.GetString(data, p, len * 2);
    }

    private static ushort ReadU16(byte[] data, int pos) => (ushort)(data[pos] | (data[pos + 1] << 8));

    private static uint ReadU32(byte[] data, int pos) =>
        (uint)(data[pos] | (data[pos + 1] << 8) | (data[pos + 2] << 16) | (data[pos + 3] << 24));
}
