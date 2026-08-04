using System.Text.Json;

namespace EggIncognito.Capture;

public abstract class JsonListStore<T>(string capturePath, string fileName) {
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly Lock _gate = new();

    private string FilePath => Path.Combine(capturePath, fileName);

    public IReadOnlyList<T> Load() {
        try {
            return !File.Exists(FilePath)
                ? []
                : (IReadOnlyList<T>)(JsonSerializer.Deserialize<List<T>>(File.ReadAllText(FilePath), Json) ?? []);
        } catch {
            return [];
        }
    }

    protected void Mutate(Action<List<T>> mutate) {
        lock (_gate) {
            var rows = Load().ToList();
            mutate(rows);
            try {
                Write(rows);
            } catch {
            }
        }
    }

    protected void Replace(IEnumerable<T> rows) {
        lock (_gate) {
            try {
                Write([.. rows]);
            } catch {
            }
        }
    }

    private void Write(List<T> rows) {
        Directory.CreateDirectory(capturePath);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(rows, Json));
    }
}
