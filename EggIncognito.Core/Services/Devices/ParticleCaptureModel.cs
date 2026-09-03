using System.Text.Json;
using System.Text.Json.Nodes;

namespace EggIncognito.Core.Services.Devices;

public static class ParticleCaptureModel {
    public static Model Parse(string ndjson) {
        if (string.IsNullOrWhiteSpace(ndjson))
            return new Model(false, 0, [], "empty capture log");

        var samples = new List<Sample>();
        int bad = 0;
        foreach (string line in ndjson.Split('\n', StringSplitOptions.RemoveEmptyEntries)) {
            string trimmed = line.Trim();
            if (trimmed.Length == 0) continue;
            if (TryParseLine(trimmed, out var s)) samples.Add(s);
            else bad++;
        }

        if (samples.Count == 0)
            return new Model(false, 0, [], $"no valid records ({bad} unparseable lines)");

        var clusters = samples
            .GroupBy(s => s.Mesh)
            .Select(g => Summarize(g.Key, [.. g]))
            .OrderByDescending(c => c.Count)
            .ToList();

        string diag = bad > 0 ? $"{bad} unparseable lines skipped" : "ok";
        return new Model(true, samples.Count, clusters, diag);
    }

    private static bool TryParseLine(string line, out Sample s) {
        s = default;
        try {
            var node = JsonNode.Parse(line);
            if (node is not JsonObject o) return false;
            string? mesh = o["mesh"]?.GetValue<string>();
            var xs = o["x"]?.AsArray();
            if (mesh is null || xs is null || xs.Count < 12) return false;

            float[] t = new float[12];
            for (int i = 0; i < 12; i++) t[i] = (float)xs[i]!.GetValue<double>();

            foreach (float v in t) {
                if (float.IsNaN(v) || float.IsInfinity(v))
                    return false;
            }

            float size = o["s"] is { } sn ? (float)sn.GetValue<double>() : 0f;
            int tIdx = o["t"] is { } tn ? tn.GetValue<int>() : 0;
            s = new Sample(tIdx, mesh, t, size);
            return true;
        } catch (JsonException) {
            return false;
        } catch (InvalidOperationException) {
            return false;
        } catch (FormatException) {
            return false;
        }
    }

    private static Cluster Summarize(string mesh, List<Sample> samples) {
        int n = samples.Count;
        double cx = 0, cy = 0, cz = 0, sizeSum = 0;
        float[] minP = [float.MaxValue, float.MaxValue, float.MaxValue];
        float[] maxP = [float.MinValue, float.MinValue, float.MinValue];

        foreach (var s in samples) {
            (float px, float py, float pz) = Translation(s.Transform);
            cx += px;
            cy += py;
            cz += pz;
            sizeSum += s.Size;
            minP[0] = Math.Min(minP[0], px);
            maxP[0] = Math.Max(maxP[0], px);
            minP[1] = Math.Min(minP[1], py);
            maxP[1] = Math.Max(maxP[1], py);
            minP[2] = Math.Min(minP[2], pz);
            maxP[2] = Math.Max(maxP[2], pz);
        }

        float[] centroid = [(float)(cx / n), (float)(cy / n), (float)(cz / n)];

        double radSum = 0;
        foreach (var s in samples) {
            (float px, _, float pz) = Translation(s.Transform);
            float dx = px - centroid[0];
            float dz = pz - centroid[2];
            radSum += Math.Sqrt(dx * dx + dz * dz);
        }

        float[] bob = [maxP[0] - minP[0], maxP[1] - minP[1], maxP[2] - minP[2]];
        return new Cluster(mesh, n, centroid, (float)(radSum / n), bob, (float)(sizeSum / n));
    }

    private static (float X, float Y, float Z) Translation(float[] m) => (m[9], m[10], m[11]);

    public readonly record struct Sample(int T, string Mesh, float[] Transform, float Size);

    public readonly record struct Cluster(
        string Mesh,
        int Count,
        float[] Centroid,
        float Radius,
        float[] BobSpan,
        float MeanSize) {
        public JsonObject ToJson() => new() {
            ["mesh"] = Mesh,
            ["count"] = Count,
            ["centroid"] = new JsonArray(Centroid[0], Centroid[1], Centroid[2]),
            ["radius"] = Radius,
            ["bobSpan"] = new JsonArray(BobSpan[0], BobSpan[1], BobSpan[2]),
            ["meanSize"] = MeanSize
        };
    }

    public readonly record struct Model(
        bool Ok,
        int TotalSamples,
        IReadOnlyList<Cluster> Clusters,
        string Diagnostics) {
        public Cluster? Dominant => Clusters.Count == 0 ? null : Clusters[0];

        public JsonObject ToJson() => new() {
            ["ok"] = Ok,
            ["totalSamples"] = TotalSamples,
            ["clusters"] = new JsonArray(Clusters.Select(c => (JsonNode)c.ToJson()).ToArray()),
            ["dominant"] = Dominant is { } d ? d.ToJson() : null,
            ["diagnostics"] = Diagnostics
        };
    }
}
