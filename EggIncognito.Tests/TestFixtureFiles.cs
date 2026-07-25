namespace EggIncognito.Tests;

public static class TestFixtureFiles {
    public static bool TryRead(string name, out byte[] bytes) {
        foreach (string rel in new[] {
                     "../../../../captures/fixtures", "../../../../../captures/fixtures",
                     "../../../../EggIncognito/captures/fixtures"
                 }) {
            string full = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, rel, name));
            if (File.Exists(full)) {
                bytes = File.ReadAllBytes(full);
                return true;
            }
        }

        bytes = [];
        return false;
    }
}
