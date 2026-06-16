using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Build;

// Offline command, not a user-facing feature. Reads a decrypted Egg Inc iOS Mach-O binary, carves the
// embedded FileDescriptorProto, and writes the reconstructed .proto. Invoked as
// `dotnet run -- __extract-ios-proto <binaryPath> <outPath>`; exits without booting the web host.
// Mirrors the TypeEmitter build-command shape. STATIC read of the binary only; it is never executed.
public static class IosProtoExtractor
{
    // Returns 0 on success, nonzero on failure (missing file / no descriptor / parse failure), printing
    // the diagnostic so a script can see why.
    public static int Run(string binaryPath, string outPath)
    {
        if (!File.Exists(binaryPath))
        {
            Console.Error.WriteLine($"__extract-ios-proto: binary not found: {binaryPath}");
            return 1;
        }

        byte[] macho;
        try { macho = File.ReadAllBytes(binaryPath); }
        catch (Exception e) { Console.Error.WriteLine($"__extract-ios-proto: read failed: {e.Message}"); return 1; }

        var result = MachoProtoExtractor.Extract(macho);
        Console.Error.WriteLine($"__extract-ios-proto: {result.Diagnostics}");
        if (!result.Ok || result.Proto is null)
            return 2;

        File.WriteAllText(outPath, result.Proto);
        Console.Error.WriteLine($"__extract-ios-proto: wrote {result.Proto.Length} chars to {outPath}");
        return 0;
    }
}
