using System.Diagnostics;
using System.Text.Json;

namespace EggIncognito.Runner.Extract;

// Shells the vendored client_version.py over the arm split. It disassembles libegginc.so and picks the
// compiled-in clientVersion using previousClientVersion as the anchor. Null when prev is unknown (no
// anchor) or the tool cannot determine a value. extractorRepo + python match PbtkProtoExtractor.
public sealed class LibegincClientVersionReader(string extractorRepo, string python) : IClientVersionReader
{
    public string? Read(string apkPath, int? previousClientVersion)
    {
        if (previousClientVersion is null) return null;
        try
        {
            var psi = new ProcessStartInfo(python)
            {
                WorkingDirectory = extractorRepo,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            psi.ArgumentList.Add("-W");
            psi.ArgumentList.Add("ignore");
            psi.ArgumentList.Add(Path.Combine("pbtk", "extractors", "client_version.py"));
            psi.ArgumentList.Add(apkPath);
            psi.ArgumentList.Add(previousClientVersion.Value.ToString());
            using var p = Process.Start(psi);
            if (p is null) return null;
            var stdout = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            if (p.ExitCode != 0) return null;
            using var doc = JsonDocument.Parse(stdout);
            return doc.RootElement.TryGetProperty("clientVersion", out var v) && v.ValueKind == JsonValueKind.Number
                ? v.GetInt32().ToString()
                : null;
        }
        catch
        {
            return null;
        }
    }
}
