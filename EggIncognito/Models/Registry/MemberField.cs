namespace EggIncognito.Models.Registry;

public sealed class MemberField {
    public string Platform { get; set; } = "";
    public string OriginalBuild { get; set; } = "";
    public string Build { get; set; } = "";
    public string ClientVersion { get; set; } = "";
    public string Source { get; set; } = "";
    public bool ProtoOpen { get; set; }
    public bool ProtoBusy { get; set; }
    public string Proto { get; set; } = "";
}
