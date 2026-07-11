using Discord;
using EggIncognito.Services;

namespace EggIncognito.Bot;

// Pure embed builders for each command (no Discord client / no live service reads).
public static class BotEmbeds
{
    private const uint Accent = 0xEF7559;
    private static string Bytes(long n) =>
        n < 1024 ? $"{n} B" : n < 1024 * 1024 ? $"{n / 1024.0:0.0} KB" : $"{n / (1024.0 * 1024):0.0} MB";
    private static string Up(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes}m" : $"{t.Minutes}m {t.Seconds}s";

    public static Embed Status(StatusSnapshot s)
    {
        var b = new EmbedBuilder()
            .WithTitle("EggIncognito - status")
            .WithColor(new Color(Accent))
            .AddField("Mode", s.Mode, inline: true)
            .AddField("DB layer", s.DbEnabled ? "on" : "off", inline: true)
            .AddField("Signing", s.SigningReady ? "ready" : "disabled", inline: true)
            .AddField("Capture", s.CaptureState, inline: true)
            .AddField("Uptime", Up(s.Uptime), inline: true);
        if (s.CaptureRunning)
        {
            b.AddField("Flows", s.FlowsCaptured.ToString(), inline: true)
             .AddField("Devices", s.DeviceCount.ToString(), inline: true)
             .AddField("Captured", Bytes(s.BytesCaptured), inline: true);
        }
        return b.Build();
    }

    public static Embed Endpoints(StatusSnapshot s) =>
        new EmbedBuilder()
            .WithTitle("Endpoint coverage")
            .WithColor(new Color(Accent))
            .AddField("ok", s.EndpointsOk.ToString(), inline: true)
            .AddField("empty", s.EndpointsEmpty.ToString(), inline: true)
            .AddField("missing", s.EndpointsMissing.ToString(), inline: true)
            .Build();

    public static Embed Health(TimeSpan uptime) =>
        new EmbedBuilder()
            .WithTitle("pong")
            .WithDescription($"online - uptime {Up(uptime)}")
            .WithColor(new Color(Accent))
            .Build();

    public static Embed Error(string message) =>
        new EmbedBuilder().WithTitle("Something went wrong").WithDescription(message).WithColor(0xED4245).Build();
}
