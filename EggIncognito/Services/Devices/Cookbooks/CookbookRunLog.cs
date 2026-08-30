using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices.Cookbooks;

public sealed class CookbookRunLog(Action<string> progress) {
    private readonly List<string> _lines = [];

    public IReadOnlyList<string> Lines => _lines;

    public void Add(string line) {
        _lines.Add(line);
        progress(line);
    }

    public void AddRange(IEnumerable<string> lines) {
        foreach (string line in lines) Add(line);
    }

    public DeviceCookbookRun Ok(string cookbookId, string? note = null) =>
        new(true, cookbookId, Lines, null, note);

    public DeviceCookbookRun Fail(string cookbookId, string step, string note) {
        Add(note);
        return new DeviceCookbookRun(false, cookbookId, Lines, step, note);
    }
}
