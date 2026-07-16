namespace EggIncognito.Bot;
public enum BotCommand { Health, Status, Verify, Endpoints, Proto, UpdateServer, Unknown }
public sealed record ProtoArgs(bool IsList, int Page, string? TypeName, string? Error)
{
    public static ProtoArgs ListPage(int page) => new(true, page, null, null);
    public static ProtoArgs ForType(string name) => new(false, 0, name, null);
    public static ProtoArgs Invalid(string error) => new(false, 0, null, error);
}

public static class CommandParsing
{
    public static BotCommand Resolve(string? name) => name switch
    {
        "health" => BotCommand.Health,
        "status" => BotCommand.Status,
        "verify" => BotCommand.Verify,
        "endpoints" => BotCommand.Endpoints,
        "proto" => BotCommand.Proto,
        "updateserver" => BotCommand.UpdateServer,
        _ => BotCommand.Unknown,
    };

   
   
    public static ProtoArgs ParseProto(string? subcommand, IReadOnlyList<(string Name, object? Value)> options)
    {
        switch (subcommand)
        {
            case "list":
                var raw = options.FirstOrDefault(o => o.Name == "page").Value;
                var page = raw switch { long l => (int)l, int i => i, _ => 1 };
                return ProtoArgs.ListPage(page);
            case "type":
                var name = options.FirstOrDefault(o => o.Name == "name").Value as string;
                return string.IsNullOrWhiteSpace(name)
                    ? ProtoArgs.Invalid("Missing required option `name`. Use `/proto type name:<MessageType>`.")
                    : ProtoArgs.ForType(name);
            case null or "":
                return ProtoArgs.Invalid("Missing subcommand. Use `/proto list` or `/proto type`.");
            default:
                return ProtoArgs.Invalid($"Unknown subcommand `{subcommand}`.");
        }
    }
}
