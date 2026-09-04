using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.Models.Devices;

namespace EggIncognito.Services.Devices;

public sealed class JobGroupCollapser(int take) {
    public const int DefaultTake = 20;

    private readonly List<JobGroupRow> _groups = [];
    private DeviceJobRow? _newest;
    private DeviceJobRow? _oldest;
    private int _repeat;
    private long _sealedOldestId;
    private long? _next;

    public bool Complete => _groups.Count >= take;

    public static int ClampTake(int take) => Math.Clamp(take, 1, 100);

    public static int BatchFor(int take) => Math.Clamp(take * 4, 40, 200);

    public static bool Folds(DeviceJobRow head, DeviceJobRow row) =>
        head.State != DeviceJobStates.Running
        && row.State != DeviceJobStates.Running
        && string.Equals(head.Kind, row.Kind, StringComparison.Ordinal)
        && string.Equals(head.State, row.State, StringComparison.Ordinal)
        && string.Equals(head.Trigger, row.Trigger, StringComparison.Ordinal)
        && string.Equals(head.Outcome, row.Outcome, StringComparison.Ordinal);

    public void Add(DeviceJobRow row) {
        if (Complete) return;
        if (_newest is { } head && Folds(head, row)) {
            _oldest = row;
            _repeat++;
            return;
        }

        Seal();
        if (Complete) {
            _next = _sealedOldestId;
            return;
        }

        _newest = row;
        _oldest = row;
        _repeat = 1;
    }

    public void Feed(IEnumerable<DeviceJobRow> rows) {
        foreach (var row in rows) {
            Add(row);
            if (Complete) return;
        }
    }

    public JobPage Finish() {
        if (!Complete) Seal();
        return new JobPage(_groups, _next);
    }

    private void Seal() {
        if (_newest is not { } newest || _oldest is not { } oldest) return;
        _groups.Add(new JobGroupRow(
            newest.Id, newest.Kind, newest.State, newest.Trigger,
            oldest.StartedAt, newest.FinishedAt,
            newest.Outcome, newest.Message, newest.AppVersion, newest.Build, newest.Revision,
            _repeat, newest.StartedAt));
        _sealedOldestId = oldest.Id;
        _newest = null;
        _oldest = null;
        _repeat = 0;
    }
}
