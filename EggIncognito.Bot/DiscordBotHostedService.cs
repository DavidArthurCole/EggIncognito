using Discord;
using Discord.WebSocket;
using EggIncognito.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EggIncognito.Bot;

// Owns the Discord gateway connection for the bot's lifetime. Opt-in: only registered when
// Discord:BotToken is set. Connects, sets a static "Playing EggIncognito" presence, registers the
// global (+ optional guild) commands on Ready, and routes interactions. Never throws out of
// StartAsync - a bot failure logs + leaves the web app running.
public sealed class DiscordBotHostedService(
    BotOptions options,
    IStatusProvider status,
    IProtoReflection proto,
    ILogger<DiscordBotHostedService> logger) : IHostedService
{
    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;
    private DiscordSocketClient? _client;
    private InteractionRouter? _router;
    private bool _commandsRegistered;

    public async Task StartAsync(CancellationToken ct)
    {
        try
        {
            _client = new DiscordSocketClient(new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.None,
                LogLevel = LogSeverity.Info,
            });
            _router = new InteractionRouter(status, proto, _startedAt, logger);

            _client.Log += msg => { logger.LogInformation("discord: {Message}", msg.ToString()); return Task.CompletedTask; };
            _client.SlashCommandExecuted += cmd => _router!.HandleSlashAsync(cmd);
            _client.AutocompleteExecuted += ac => _router!.HandleAutocompleteAsync(ac);
            _client.Ready += OnReadyAsync;

            await _client.LoginAsync(TokenType.Bot, options.Token);
            await _client.StartAsync();
            logger.LogInformation("bot: gateway connecting...");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "bot: failed to start - continuing without the bot");
        }
    }

    private async Task OnReadyAsync()
    {
        try
        {
            await _client!.SetGameAsync("EggIncognito");
            await _client.SetStatusAsync(UserStatus.Online);

            await EnsureSharedRoleAsync();

            // Register commands once. Ready re-fires on every reconnect/resume; re-pushing the global
            // catalog each time would burn Discord's global-command write rate limit for no gain.
            if (_commandsRegistered) return;

            var commands = CommandDefinitions.BuildAll();
            await _client.BulkOverwriteGlobalApplicationCommandsAsync(commands);
            if (!string.IsNullOrWhiteSpace(options.GuildId) && ulong.TryParse(options.GuildId, out var gid))
            {
                IGuild? guild = _client.GetGuild(gid);
                if (guild is not null) await guild.BulkOverwriteApplicationCommandsAsync(commands);
            }
            _commandsRegistered = true;
            logger.LogInformation("bot: ready - presence set + {Count} commands registered", commands.Length);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "bot: OnReady failed (presence/command registration)");
        }
    }

    // Grants options.SharedRoleId to the bot's own member in the configured guild. Best-effort:
    // any failure (missing Manage Roles, role above the bot in the hierarchy, cache miss, REST error)
    // logs a warning and returns. No-op when GuildId or SharedRoleId is unset or unparseable, or when
    // the member already has the role. Idempotent - safe to call on every Ready.
    private async Task EnsureSharedRoleAsync()
    {
        if (string.IsNullOrWhiteSpace(options.GuildId) || string.IsNullOrWhiteSpace(options.SharedRoleId)) return;
        if (!ulong.TryParse(options.GuildId, out var gid) || !ulong.TryParse(options.SharedRoleId, out var rid)) return;
        try
        {
            var guild = _client!.GetGuild(gid);
            if (guild is null) { logger.LogWarning("bot: shared-role: guild {Guild} not available", gid); return; }
            var self = guild.GetUser(_client.CurrentUser.Id);
            if (self is null) { logger.LogWarning("bot: shared-role: self member not found in guild {Guild}", gid); return; }
            if (!BotRoles.NeedsRole(self.Roles.Select(r => r.Id), rid)) return;
            var role = guild.GetRole(rid);
            if (role is null) { logger.LogWarning("bot: shared-role: role {Role} not found in guild {Guild}", rid, gid); return; }
            await self.AddRoleAsync(role);
            logger.LogInformation("bot: shared-role: assigned {Role} in guild {Guild}", rid, gid);
        }
        catch (Exception ex) { logger.LogWarning(ex, "bot: shared-role: assign failed"); }
    }

    public async Task StopAsync(CancellationToken ct)
    {
        if (_client is null) return;
        try
        {
            await _client.SetStatusAsync(UserStatus.Offline);
            await _client.StopAsync();
            await _client.LogoutAsync();
        }
        catch (Exception ex) { logger.LogWarning(ex, "bot: error during shutdown"); }
        finally { _client.Dispose(); }
    }
}
