using System.Reflection;
using EggIncognito.Components.Inspector;
using EggIncognito.Services.Workbench;
using Google.Protobuf.Reflection;

namespace EggIncognito.Services.Inspector;

public sealed class RinfoSeed {
    public string EiUserId { get; set; } = "";
    public string ClientVersion { get; set; } = "";
    public string Version { get; set; } = "";
    public string Build { get; set; } = "";
    public string Platform { get; set; } = "";
    public string Country { get; set; } = "";
    public string Language { get; set; } = "";
    public bool Debug { get; set; }

    public RinfoSeed OverlaidWith(RinfoSeed? over) {
        if (over is null) return this;
        return new RinfoSeed {
            EiUserId = Pick(EiUserId, over.EiUserId),
            ClientVersion = Pick(ClientVersion, over.ClientVersion),
            Version = Pick(Version, over.Version),
            Build = Pick(Build, over.Build),
            Platform = Pick(Platform, over.Platform),
            Country = Pick(Country, over.Country),
            Language = Pick(Language, over.Language),
            Debug = over.Debug || Debug
        };
    }

    private static string Pick(string under, string over) =>
        string.IsNullOrWhiteSpace(over) ? under : over;
}

public sealed class InspectorState : WorkbenchStateBase {
    private static readonly WorkbenchMode[] ReaderModes = [
        new(InspectorRefParser.ResultMode, "Result", "The transaction you built and sent"),
        new(InspectorRefParser.ReferenceMode, "Reference", "Documentation for the current subject")
    ];

    public static readonly string[] PlatformOptions = [
        .. typeof(Ei.Platform).GetFields()
            .Where(f => f.IsLiteral)
            .Select(f => f.GetCustomAttribute<OriginalNameAttribute>()?.Name ?? f.Name)
    ];

    public override IReadOnlyList<WorkbenchMode> Modes => ReaderModes;

    public InspectorReaderMode ReaderMode {
        get => Mode == InspectorRefParser.ReferenceMode
            ? InspectorReaderMode.Reference
            : InspectorReaderMode.Result;
        set => Mode = value == InspectorReaderMode.Reference
            ? InspectorRefParser.ReferenceMode
            : InspectorRefParser.ResultMode;
    }

    public InspectorRailList RailList { get; set; } = InspectorRailList.Endpoints;

    public RouteInfo? Selected { get; set; }
    public string? SelectedObject { get; set; }

    public List<EnvRow> EnvRows { get; set; } = [];
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

    public InspectorRef Ref() => new(Selected?.Path, SelectedObject, ReaderMode);

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
                Key = "build", ValueType = EnvValueType.String, Editor = EnvEditor.Int,
                Hint = "integer", Value = Rinfo.Build
            },
            new EnvRow {
                Key = "platform", ValueType = EnvValueType.String, Editor = EnvEditor.Select,
                Options = PlatformOptions, Value = Rinfo.Platform
            },
            new EnvRow {
                Key = "country", ValueType = EnvValueType.String, Editor = EnvEditor.Code,
                Hint = "2-letter code", Value = Rinfo.Country
            },
            new EnvRow {
                Key = "language", ValueType = EnvValueType.String, Editor = EnvEditor.Code,
                Hint = "2-letter code", Value = Rinfo.Language
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
}
