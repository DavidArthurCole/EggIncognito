using System.Diagnostics;
using System.Text;
using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Runner.Extract;

// Runs jar_extract.py (pbtk) over the arm split, then cleans via in-process ProtoCleanup.
// extractorRepo = tools/proto-extract checkout; python = its venv interpreter.
public sealed class PbtkProtoExtractor(string extractorRepo, string python) : IProtoExtractor
{
    public byte[] Extract(string apkPath)
    {
        var outDir = Path.Combine(Path.GetTempPath(), "egg-proto-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outDir);
        try
        {
            RunPython(Path.Combine("pbtk", "extractors", "jar_extract.py"), apkPath, outDir);
            var eiPath = Path.Combine(outDir, "ei.proto");
            var commonPath = Path.Combine(outDir, "common.proto");
            if (!File.Exists(eiPath))
                throw new InvalidOperationException($"pbtk produced no ei.proto in {outDir}");
            var ei = File.ReadAllText(eiPath);
            var common = File.Exists(commonPath) ? File.ReadAllText(commonPath) : "";
            return Encoding.UTF8.GetBytes(ProtoCleanup.Clean(ei, common));
        }
        finally
        {
            try { Directory.Delete(outDir, recursive: true); } catch { /* best-effort temp cleanup */ }
        }
    }

    private void RunPython(string script, params string[] args)
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
        psi.ArgumentList.Add(script);
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("failed to start python");
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0)
            throw new InvalidOperationException($"{script} exit {p.ExitCode}: {stderr.Trim()}\n{stdout.Trim()}");
    }
}
