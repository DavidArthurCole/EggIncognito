using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Build;

public static class IosProtoExtractor
{
    public static int Run(string binaryPath, string outPath)
    {
        if (!File.Exists(binaryPath))
        {
            Console.Error.WriteLine($"__extract-proto: binary not found: {binaryPath}");
            return 1;
        }

        byte[] bytes;
        try { bytes = File.ReadAllBytes(binaryPath); }
        catch (Exception e) { Console.Error.WriteLine($"__extract-proto: read failed: {e.Message}"); return 1; }

       
        bool isZip = bytes.Length > 4 && bytes[0] == 0x50 && bytes[1] == 0x4B && bytes[2] == 0x03 && bytes[3] == 0x04;
        var result = isZip ? ArchiveProtoExtractor.Extract(bytes) : DescriptorProtoCarver.Extract(bytes);
        Console.Error.WriteLine($"__extract-proto: {result.Diagnostics}"
            + (result.AppVersion is { } v ? $" appVersion={v}" : "")
            + (result.Build is { } b ? $" build={b}" : ""));
        if (!result.Ok || result.Proto is null)
            return 2;

        File.WriteAllText(outPath, result.Proto);
        Console.Error.WriteLine($"__extract-proto: wrote {result.Proto.Length} chars to {outPath}");
        return 0;
    }
}
