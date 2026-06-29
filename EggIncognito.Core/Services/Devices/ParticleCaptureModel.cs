using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace EggIncognito.Services.Devices;

// Parses the NDJSON the frida hook (tools/ios-frida/particle-capture.js) writes on the phone: one record per
// ParticleBatchedMesh::addParticle call = { t, mesh, x:[12 floats], s }. The transform is a column-major 3x4
// affine; cells 9/10/11 (the 4th column) are the world translation.
//
// The hatchery effect is one of several live particle streams; the mesh pointer separates them. We cluster by
// mesh, pick the dominant cluster (most particles at rest = the floating hatchery sparkle), and summarize its
// geometry as world-space stats the host can fit or bake. No model authored here; this READS the captured
// motion so the renderer can replay it (EXTRACT-not-author).
//
// Pure: takes the log text, returns a model. No device, no ssh. Testable from a fixture.
public static class ParticleCaptureModel
{
    // One captured addParticle call.
    public readonly record struct Sample(int T, string Mesh, float[] Transform, float Size);

    // A single particle stream (one mesh pointer): its samples + world-space summary stats.
    public readonly record struct Cluster(
        string Mesh, int Count, float[] Centroid, float Radius, float[] BobSpan, float MeanSize)
    {
        public JsonObject ToJson() => new()
        {
            ["mesh"] = Mesh,
            ["count"] = Count,
            ["centroid"] = new JsonArray(Centroid[0], Centroid[1], Centroid[2]),
            ["radius"] = Radius,
            ["bobSpan"] = new JsonArray(BobSpan[0], BobSpan[1], BobSpan[2]),
            ["meanSize"] = MeanSize,
        };
    }

    public readonly record struct Model(bool Ok, int TotalSamples, IReadOnlyList<Cluster> Clusters, string Diagnostics)
    {
        // The dominant cluster = the most-sampled stream = the resting-farm effect we want.
        public Cluster? Dominant => Clusters.Count == 0 ? null : Clusters[0];

        public JsonObject ToJson() => new()
        {
            ["ok"] = Ok,
            ["totalSamples"] = TotalSamples,
            ["clusters"] = new JsonArray(Clusters.Select(c => (JsonNode)c.ToJson()).ToArray()),
            ["dominant"] = Dominant is { } d ? d.ToJson() : null,
            ["diagnostics"] = Diagnostics,
        };
    }

    public static Model Parse(string ndjson)
    {
        if (string.IsNullOrWhiteSpace(ndjson))
            return new(false, 0, [], "empty capture log");

        var samples = new List<Sample>();
        int bad = 0;
        foreach (var line in ndjson.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0) continue;
            if (TryParseLine(trimmed, out var s)) samples.Add(s);
            else bad++;
        }

        if (samples.Count == 0)
            return new(false, 0, [], $"no valid records ({bad} unparseable lines)");

        var clusters = samples
            .GroupBy(s => s.Mesh)
            .Select(g => Summarize(g.Key, g.ToList()))
            .OrderByDescending(c => c.Count)
            .ToList();

        var diag = bad > 0 ? $"{bad} unparseable lines skipped" : "ok";
        return new(true, samples.Count, clusters, diag);
    }

    private static bool TryParseLine(string line, out Sample s)
    {
        s = default;
        try
        {
            var node = JsonNode.Parse(line);
            if (node is not JsonObject o) return false;
            var mesh = o["mesh"]?.GetValue<string>();
            var xs = o["x"]?.AsArray();
            if (mesh is null || xs is null || xs.Count < 12) return false;

            var t = new float[12];
            for (int i = 0; i < 12; i++) t[i] = (float)xs[i]!.GetValue<double>();
            // any NaN/Inf cell (a bad pointer read on the phone) => drop the record.
            foreach (var v in t) if (float.IsNaN(v) || float.IsInfinity(v)) return false;

            var size = o["s"] is { } sn ? (float)sn.GetValue<double>() : 0f;
            var tIdx = o["t"] is { } tn ? tn.GetValue<int>() : 0;
            s = new Sample(tIdx, mesh, t, size);
            return true;
        }
        catch (JsonException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (FormatException) { return false; }
    }

    // World-space summary of one stream. Translation = the affine's 4th column (cells 9,10,11 column-major).
    // Centroid = mean position. Radius = mean horizontal distance from the centroid (the float ring extent).
    // BobSpan = per-axis (max - min) of position (the bounding extent of the motion).
    private static Cluster Summarize(string mesh, List<Sample> samples)
    {
        int n = samples.Count;
        double cx = 0, cy = 0, cz = 0, sizeSum = 0;
        var minP = new[] { float.MaxValue, float.MaxValue, float.MaxValue };
        var maxP = new[] { float.MinValue, float.MinValue, float.MinValue };

        foreach (var s in samples)
        {
            var (px, py, pz) = Translation(s.Transform);
            cx += px; cy += py; cz += pz; sizeSum += s.Size;
            minP[0] = Math.Min(minP[0], px); maxP[0] = Math.Max(maxP[0], px);
            minP[1] = Math.Min(minP[1], py); maxP[1] = Math.Max(maxP[1], py);
            minP[2] = Math.Min(minP[2], pz); maxP[2] = Math.Max(maxP[2], pz);
        }

        var centroid = new[] { (float)(cx / n), (float)(cy / n), (float)(cz / n) };

        double radSum = 0;
        foreach (var s in samples)
        {
            var (px, _, pz) = Translation(s.Transform);
            var dx = px - centroid[0];
            var dz = pz - centroid[2];
            radSum += Math.Sqrt(dx * dx + dz * dz);
        }

        var bob = new[] { maxP[0] - minP[0], maxP[1] - minP[1], maxP[2] - minP[2] };
        return new Cluster(mesh, n, centroid, (float)(radSum / n), bob, (float)(sizeSum / n));
    }

    // Column-major 3x4 affine: column j = cells[3j..3j+2]; the 4th column (j=3) = translation = cells 9,10,11.
    private static (float X, float Y, float Z) Translation(float[] m) => (m[9], m[10], m[11]);
}
