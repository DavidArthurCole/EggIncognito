namespace EggIncognito.Services.Notifications;

public sealed class NotificationDraft {
    public string Url { get; set; } = "";
    public string EventKind { get; set; } = "proto_build";
    public string Trigger { get; set; } = "version_up";
    public bool Android { get; set; } = true;
    public bool Ios { get; set; } = true;
    public bool Active { get; set; } = true;
    public string MessageTemplate { get; set; } = "";
    public HashSet<string> Filters { get; set; } = [];
}

public static class NotificationModes {
    public const string Config = "config";
    public const string Preview = "preview";
    public const string History = "history";

    public static readonly IReadOnlyList<(string Key, string Label)> All = [
        (Config, "Config"), (Preview, "Preview"), (History, "History")
    ];

    public static string Normalize(string? mode) =>
        All.Any(m => m.Key == mode) ? mode! : Config;
}

public sealed class NotificationsWorkbenchState {
    public int? SelectedId { get; set; }
    public bool Creating { get; set; } = true;
    public string Mode { get; set; } = NotificationModes.Config;
    public string RailFilter { get; set; } = "";
    public string SampleKey { get; set; } = "";
    public NotificationDraft NewDraft { get; } = new();
    public Dictionary<int, NotificationDraft> Edits { get; } = [];

    public NotificationDraft Draft(int? id) =>
        id is { } key ? Edits.TryGetValue(key, out var d) ? d : Edits[key] = new NotificationDraft() : NewDraft;

    public NotificationDraft Active() => Creating ? NewDraft : Draft(SelectedId);

    public void ResetNew() {
        NewDraft.Url = "";
        NewDraft.EventKind = "proto_build";
        NewDraft.Trigger = "version_up";
        NewDraft.Android = true;
        NewDraft.Ios = true;
        NewDraft.Active = true;
        NewDraft.MessageTemplate = "";
        NewDraft.Filters = [];
    }

    public string Hash() {
        if (Creating) return "notify";
        if (SelectedId is not { } id) return "notify";
        return Mode == NotificationModes.Config ? $"notify_{id}" : $"notify_{id}_{Mode}";
    }

    public static (bool Match, int? Id, string Mode) ParseHash(string? hash) {
        string body = (hash ?? "").TrimStart('#');
        if (body.Length == 0) return (false, null, NotificationModes.Config);
        string[] parts = body.Split('_');
        if (parts[0] != "notify") return (false, null, NotificationModes.Config);
        if (parts.Length < 2) return (true, null, NotificationModes.Config);
        if (!int.TryParse(parts[1], out int id)) return (true, null, NotificationModes.Config);
        return (true, id, NotificationModes.Normalize(parts.Length > 2 ? parts[2] : null));
    }
}
