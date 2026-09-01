namespace EggIncognito.Services.Admin;

public sealed record AdminPane(string Key, string Group, string Label);

public static class AdminPanes {
    public const string Traffic = "traffic";
    public const string DeviceStatus = "device-status";
    public const string Users = "users";
    public const string Notifications = "notifications";
    public const string ThemePolicy = "theme-policy";
    public const string DataStatus = "data-status";
    public const string Binaries = "binaries";
    public const string GameData = "game-data";
    public const string Events = "events";
    public const string Contracts = "contracts";
    public const string Staged = "staged";
    public const string Contributions = "contributions";
    public const string Sessions = "sessions";
    public const string Console = "console";
    public const string BotConfig = "bot-config";
    public const string Maintenance = "maintenance";
    public const string VirtualDevices = "virtual-devices";
    public const string Playground = "playground";

    public static readonly IReadOnlyList<AdminPane> All = [
        new(Traffic, "Overview", "Traffic"),
        new(DeviceStatus, "Overview", "Devices"),
        new(Users, "Access", "Users"),
        new(Notifications, "Access", "Notifications"),
        new(ThemePolicy, "Access", "Theme policy"),
        new(DataStatus, "Data", "Data status"),
        new(Binaries, "Data", "Binaries"),
        new(GameData, "Data", "Game data"),
        new(Events, "Data", "Events"),
        new(Contracts, "Data", "Contracts"),
        new(Staged, "Data", "Staged"),
        new(Contributions, "Data", "Contributions"),
        new(Sessions, "Ops", "Sessions"),
        new(Console, "Ops", "Console"),
        new(BotConfig, "Ops", "Bot config"),
        new(Maintenance, "Ops", "Maintenance"),
        new(VirtualDevices, "Ops", "Virtual devices"),
        new(Playground, "Ops", "Playground")
    ];
}
