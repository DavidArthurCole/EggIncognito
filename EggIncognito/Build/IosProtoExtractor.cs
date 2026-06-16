using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Build;

// Offline command, not a user-facing feature. Reads a decrypted Egg Inc binary (iOS Mach-O, Android APK,
// or a bare native .so), carves the embedded FileDescriptorProto, and writes the reconstructed .proto.
// Invoked as `dotnet run -- __extract-proto <binaryPath> <outPath>`; exits without booting the web host.
// Auto-detects an APK (zip) vs a raw binary. STATIC read only; the binary is never executed.
public static class IosProtoExtractor
{
    // Returns 0 on success, nonzero on failure (missing file / no descriptor / parse failure), printing
    // the diagnostic so a script can see why.
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

        // APK + IPA are both zips (PK\x03\x04); the archive extractor finds the native binary entry
        // inside and carves it. A non-zip is a raw binary (bare Mach-O / .so).
        bool isZip = bytes.Length > 4 && bytes[0] == 0x50 && bytes[1] == 0x4B && bytes[2] == 0x03 && bytes[3] == 0x04;
        var result = isZip ? ArchiveProtoExtractor.Extract(bytes) : DescriptorProtoCarver.Extract(bytes);
        Console.Error.WriteLine($"__extract-proto: {result.Diagnostics}");
        if (!result.Ok || result.Proto is null)
            return 2;

        File.WriteAllText(outPath, result.Proto);
        Console.Error.WriteLine($"__extract-proto: wrote {result.Proto.Length} chars to {outPath}");
        return 0;
    }
}
