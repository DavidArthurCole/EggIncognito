using System.Reflection;

namespace EggIncognito.DeviceTools;

public static class DeviceScripts {
    public static string ParticleCapture => Read("particle-capture.js");
    public static string ParticleDiscover => Read("particle-discover.js");

    private static string Read(string fileName) {
        var asm = typeof(DeviceScripts).Assembly;
        var name = Array.Find(asm.GetManifestResourceNames(), n => n.EndsWith(fileName, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"embedded device script not found: {fileName}");
        using var stream = asm.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
