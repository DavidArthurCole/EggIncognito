namespace EggIncognito.Bot;

// Top-level slash commands the router knows how to dispatch.
public enum BotCommand { Health, Status, Verify, Endpoints, Proto, Unknown }

// Parsed /proto invocation. Built via the factories; Error is non-null for malformed payloads.
public sealed record ProtoArgs(bool IsList, int Page, string? TypeName, string? Error)
{
    public static ProtoArgs ListPage(int page) => new(true, page, null, null);
    public static ProtoArgs ForType(string name) => new(false, 0, name, null);
    public static ProtoArgs Invalid(string error) => new(false, 0, null, error);
}

// Pure dispatch + option parsing for the router, kept Discord-free so it is unit-testable.
// The router lowers gateway payloads to primitives before calling in.
public static class CommandParsing
{
    public static BotCommand Resolve(string? name) => name switch
    {
        "health" => BotCommand.Health,
        "status" => BotCommand.Status,
        "verify" => BotCommand.Verify,
        "endpoints" => BotCommand.Endpoints,
        "proto" => BotCommand.Proto,
        _ => BotCommand.Unknown,
    };

    // subcommand = the first option's name; options = that subcommand's nested (name, value) pairs.
    // Never throws on a malformed payload - returns ProtoArgs.Invalid with a user-facing reason.
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
