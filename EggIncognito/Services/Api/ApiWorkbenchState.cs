using System.Reflection;
using EggIncognito.Components.Capture;
using EggIncognito.Components.Inspector;
using EggIncognito.Models.Data;
using EggIncognito.Services.Inspector;
using EggIncognito.Services.Workbench;
using Google.Protobuf.Reflection;

namespace EggIncognito.Services.Api;

public sealed class ApiWorkbenchState : WorkbenchStateBase {
    public static readonly string[] PlatformOptions = [
        .. typeof(Ei.Platform).GetFields()
            .Where(f => f.IsLiteral)
            .Select(f => f.GetCustomAttribute<OriginalNameAttribute>()?.Name ?? f.Name)
    ];

    public override IReadOnlyList<WorkbenchMode> Modes => [];

    public InspectorRailList RailList { get; set; } = InspectorRailList.Endpoints;

    public RouteInfo? Selected { get; set; }
    public string? SelectedObject { get; set; }

    public List<EnvRow> EnvRows { get; set; } = [];
    public bool EnvOpen { get; set; } = true;
    public bool EnvValidated { get; set; }
    public bool EnvValidating { get; set; }
    public string? EnvError { get; set; }
    public List<FieldNode>? FieldNodes { get; set; }
    public string PathParam { get; set; } = "";
    public bool RawMode { get; set; }
    public string RawJson { get; set; } = "{}";
    public string? RawError { get; set; }

    public InspectorTarget Target { get; set; } = InspectorTarget.Mock;
    public bool Sealed { get; set; }
    public string CustomTarget { get; set; } = "";

    public bool Busy { get; set; }
    public BuildResponse? LastBuild { get; set; }
    public List<TransportStage>? BuildStages { get; set; }
    public SendResponse? Response { get; set; }
    public DiagnoseDto? Diagnosis { get; set; }
    public string SaveOut { get; set; } = "";
    public bool SaveFailed { get; set; }

    public bool HistoryEnabled { get; set; } = true;
    public List<InspectorHistoryEntry> History { get; set; } = [];

    public RinfoSeed Rinfo { get; set; } = new();
    public string[] RecentEids { get; set; } = [];
    public bool HasSalt { get; set; }
    public string? Notice { get; set; }

    public bool LiveDisabled { get; set; }
    public bool SealedAvailable { get; set; }
    public bool CanSaveDb { get; set; }
    public bool IsAdmin { get; set; }
    public bool Hosted { get; set; }

    public bool CanBuild => Selected is not null && !Busy;
    public bool CanSend => LastBuild is not null && !Busy;

    public InspectorRef Ref() => new(Selected?.Path, SelectedObject);

    public void ClearTransaction() {
        LastBuild = null;
        BuildStages = null;
        Response = null;
        Diagnosis = null;
        SaveOut = "";
        SaveFailed = false;
    }

    public void SeedEnvRows() {
        EnvRows = [
            new EnvRow {
                Key = "eiUserId", ValueType = EnvValueType.String, Editor = EnvEditor.Eid,
                Hint = "EI...", Value = Rinfo.EiUserId
            },
            new EnvRow {
                Key = "clientVersion", ValueType = EnvValueType.Number, Editor = EnvEditor.Int,
                Hint = "integer", Value = Rinfo.ClientVersion
            },
            new EnvRow {
                Key = "version", ValueType = EnvValueType.String, Editor = EnvEditor.Version,
                Hint = "major.minor.patch", Value = Rinfo.Version
            },
            new EnvRow {
                Key = "build", ValueType = EnvValueType.String, Editor = EnvEditor.Build,
                Value = Rinfo.Build
            },
            new EnvRow {
                Key = "platform", ValueType = EnvValueType.String, Editor = EnvEditor.Select,
                Options = PlatformOptions, Value = Rinfo.Platform
            },
            new EnvRow {
                Key = "country", ValueType = EnvValueType.String, Editor = EnvEditor.Code,
                Value = Rinfo.Country
            },
            new EnvRow {
                Key = "language", ValueType = EnvValueType.String, Editor = EnvEditor.Code,
                Value = Rinfo.Language
            },
            new EnvRow {
                Key = "debug", ValueType = EnvValueType.Boolean, Editor = EnvEditor.Bool,
                Value = Rinfo.Debug ? "true" : "false"
            }
        ];
    }

    public void SyncRinfoFromRow(EnvRow row) {
        switch (row.Key) {
            case "eiUserId":
                Rinfo.EiUserId = row.Value;
                break;
            case "clientVersion":
                Rinfo.ClientVersion = row.Value;
                break;
            case "version":
                Rinfo.Version = row.Value;
                break;
            case "build":
                Rinfo.Build = row.Value;
                break;
            case "platform":
                Rinfo.Platform = row.Value;
                break;
            case "country":
                Rinfo.Country = row.Value;
                break;
            case "language":
                Rinfo.Language = row.Value;
                break;
            case "debug":
                Rinfo.Debug = row.Value == "true";
                break;
        }
    }

    public void ApplyEnvLock() {
        if (FieldNodes is null) return;
        var env = EnvCollector.AsStrings(EnvRows);
        FieldTreeBuilder.ApplyEnvLock(FieldNodes, env);
        FieldTreeBuilder.ApplyEnvDefaults(FieldNodes, env);
    }

    public bool NoFillableFields =>
        FieldNodes is { Count: > 0 } && FieldNodes.All(n => n.Locked);

    public ApiSelectionKind Kind { get; set; } = ApiSelectionKind.Endpoint;

    public string Group { get; set; } = "";
    public string Id { get; set; } = "";
    public string? Sub { get; set; }
    public List<DataSourceRow>? Sources { get; set; }
#pragma warning disable IDE0028
    public Dictionary<string, string> Names { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> Formats { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, DataPayloadEntry> Payloads { get; } = new(StringComparer.Ordinal);
#pragma warning restore IDE0028
    public CaptureViewState View { get; } = new();

    public string? PendingEndpointPath { get; set; }
    public string? PendingObjectName { get; set; }

    public bool HasDataset => Kind == ApiSelectionKind.Dataset && Group.Length > 0 && Id.Length > 0;

    public string DatasetKey => Sub is { Length: > 0 } sub ? $"{Group}/{Id}/{sub}" : $"{Group}/{Id}";

    public string NameFor(string key) => Names.GetValueOrDefault(key, "");

    public void SelectDataset(string group, string id, string? sub) {
        Kind = ApiSelectionKind.Dataset;
        Group = group;
        Id = id;
        Sub = string.IsNullOrEmpty(sub) ? null : sub;
    }

    public override string HashPrefix => "api";

    public override string? Hash() {
        return Kind switch {
            ApiSelectionKind.Dataset when Group.Length > 0 && Id.Length > 0 =>
                Sub is { Length: > 0 } sub ? $"api/data/{Group}/{Id}/{sub}" : $"api/data/{Group}/{Id}",
            ApiSelectionKind.Keys => "api/keys",
            ApiSelectionKind.AllKeys => "api/keys/all",
            ApiSelectionKind.Routes => "api/routes",
            _ => MockHash()
        };
    }

    private string MockHash() {
        var formatted = InspectorRefParser.Format(Ref());
        return formatted.Length > 0 ? $"api/{formatted}" : "api";
    }

    public override bool ApplyHash(string? hash) {
        string body = (hash ?? "").TrimStart('#');
        if (body.StartsWith("data/", StringComparison.Ordinal)) body = "api/" + body;
        if (!body.StartsWith("api", StringComparison.Ordinal)) return false;
        string rest = body.Length > 3 && body[3] == '/' ? body[4..] : body == "api" ? "" : null!;
        if (rest is null) return false;

        if (rest.StartsWith("data/", StringComparison.Ordinal)) {
            string[] parts = rest["data/".Length..].Split('/');
            if (parts.Length is < 2 or > 3 || parts.Any(p => p.Length == 0 || p.Contains('.', StringComparison.Ordinal))) return false;
            SelectDataset(parts[0], parts[1], parts.Length == 3 ? parts[2] : null);
            return true;
        }

        switch (rest) {
            case "keys":
                Kind = ApiSelectionKind.Keys;
                return true;
            case "keys/all":
                Kind = ApiSelectionKind.AllKeys;
                return true;
            case "routes":
                Kind = ApiSelectionKind.Routes;
                return true;
        }

        Kind = ApiSelectionKind.Endpoint;
        var mock = InspectorRefParser.Parse(rest);
        PendingEndpointPath = mock.EndpointPath;
        PendingObjectName = mock.ObjectName;
        return true;
    }
}
