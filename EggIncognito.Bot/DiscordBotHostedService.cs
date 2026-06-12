using Discord;
using Discord.WebSocket;
using EggIncognito.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EggIncognito.Bot;

// Owns the Discord gateway connection for the bot's lifetime. Opt-in: only registered when
// Discord:BotToken is set. Connects in the background with bounded retries, sets a static
// "Playing EggIncognito" presence, registers the global (+ optional guild) commands on Ready when
// they changed, and routes interactions. Never throws out of StartAsync - a bot failure logs +
// leaves the web app running.
public sealed class DiscordBotHostedService(
    BotOptions options,
    IStatusProvider status,
    IProtoReflection proto,
    ILogger<DiscordBotHostedService> logger) : IHostedService
{
    private static readonly TimeSpan[] StartDelays =
        { TimeSpan.Zero, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(60) };

    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;
    private readonly CancellationTokenSource _stopping = new();
    private DiscordSocketClient? _client;
    private bool _commandsRegistered;

    // False until LoginAsync + StartAsync succeed; stays false when every boot attempt failed,
    // so logs/callers can tell a dead bot from a connecting one.
    public bool GatewayStarted { get; private set; }

    public Task StartAsync(CancellationToken ct)
    {
        try
        {
            var client = new DiscordSocketClient(new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.None,
                LogLevel = LogSeverity.Info,
            });
            var deploy = !string.IsNullOrWhiteSpace(options.DeployAgentUrl)
                      && !string.IsNullOrWhiteSpace(options.DeployAgentSecret)
                ? new DeployAgentClient(options.DeployAgentUrl, options.DeployAgentSecret)
                : null;
            var router = new InteractionRouter(status, proto, _startedAt, logger, deploy);

            client.Log += msg => { logger.LogInformation("discord: {Message}", msg.ToString()); return Task.CompletedTask; };
            client.SlashCommandExecuted += router.HandleSlashAsync;
            client.AutocompleteExecuted += router.HandleAutocompleteAsync;
            client.Ready += OnReadyAsync;
            _client = client;

            // Connect in the background with bounded retries: a transient boot failure (DNS blip,
            // Discord outage) must not leave the bot permanently dead or block web-app startup.
            // Once started, Discord.Net owns reconnection; the client is never disposed on failure.
            _ = ConnectWithRetryAsync(client, _stopping.Token);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "bot: failed to start - continuing without the bot");
        }
        return Task.CompletedTask;
    }

    private async Task ConnectWithRetryAsync(DiscordSocketClient client, CancellationToken ct)
    {
        for (var attempt = 0; attempt < StartDelays.Length; attempt++)
        {
            try
            {
                if (StartDelays[attempt] > TimeSpan.Zero) await Task.Delay(StartDelays[attempt], ct);
                await client.LoginAsync(TokenType.Bot, options.Token);
                await client.StartAsync();
                GatewayStarted = true;
                logger.LogInformation("bot: gateway connecting...");
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                logger.LogError(ex, "bot: gateway start attempt {Attempt}/{Max} failed",
                    attempt + 1, StartDelays.Length);
            }
        }
        logger.LogError("bot: giving up after {Max} start attempts - bot is offline, web app unaffected",
            StartDelays.Length);
    }

    private async Task OnReadyAsync()
    {
        var client = _client;
        if (client is null) return;
        try
        {
            await client.SetGameAsync("EggIncognito");
            await client.SetStatusAsync(UserStatus.Online);

            await EnsureSharedRoleAsync(client);

            // Register commands once per process. Ready re-fires on every reconnect/resume;
            // re-pushing the global catalog each time would burn Discord's global-command write
            // rate limit for no gain.
            if (_commandsRegistered) return;

            var commands = CommandDefinitions.BuildAll();
            var desired = CommandSignature.Compute(commands.Select(CommandSignature.FromProperties));

            // Skip the rate-limited bulk overwrite when Discord already holds this exact catalog.
            if (await GlobalCommandsMatchAsync(client, desired))
                logger.LogInformation("bot: global commands unchanged - skipping overwrite");
            else
                await client.BulkOverwriteGlobalApplicationCommandsAsync(commands);

            if (!string.IsNullOrWhiteSpace(options.GuildId) && ulong.TryParse(options.GuildId, out var gid))
            {
                IGuild? guild = client.GetGuild(gid);
                if (guild is not null && !await GuildCommandsMatchAsync(guild, desired))
                    await guild.BulkOverwriteApplicationCommandsAsync(commands);
            }
            _commandsRegistered = true;
            logger.LogInformation("bot: ready - presence set + {Count} commands ensured", commands.Length);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "bot: OnReady failed (presence/command registration)");
        }
    }

    // Best-effort comparison: any fetch failure logs + returns false, falling back to the overwrite.
    private async Task<bool> GlobalCommandsMatchAsync(DiscordSocketClient client, string desired)
    {
        try
        {
            var existing = await client.GetGlobalApplicationCommandsAsync();
            return CommandSignature.Compute(existing.Select(c => CommandSignature.FromCommand(c))) == desired;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "bot: could not fetch global commands - overwriting");
            return false;
        }
    }

    private async Task<bool> GuildCommandsMatchAsync(IGuild guild, string desired)
    {
        try
        {
            var existing = await guild.GetApplicationCommandsAsync();
            return CommandSignature.Compute(existing.Select(c => CommandSignature.FromCommand(c))) == desired;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "bot: could not fetch guild commands - overwriting");
            return false;
        }
    }

    // Grants options.SharedRoleId to the bot's own member in the configured guild. Best-effort:
    // any failure (missing Manage Roles, role above the bot in the hierarchy, cache miss, REST error)
    // logs a warning and returns. No-op when GuildId or SharedRoleId is unset or unparseable, or when
    // the member already has the role. Idempotent - safe to call on every Ready.
    private async Task EnsureSharedRoleAsync(DiscordSocketClient client)
    {
        if (string.IsNullOrWhiteSpace(options.GuildId) || string.IsNullOrWhiteSpace(options.SharedRoleId)) return;
        if (!ulong.TryParse(options.GuildId, out var gid) || !ulong.TryParse(options.SharedRoleId, out var rid)) return;
        try
        {
            IGuild? guild = client.GetGuild(gid);
            if (guild is null) { logger.LogWarning("bot: shared-role: guild {Guild} not available", gid); return; }
            // With GatewayIntents.None the member cache is empty, so the socket member cache misses.
            // CacheMode.AllowDownload uses the cache when present, otherwise does a REST fetch of the
            // bot's own member. No privileged GuildMembers intent is needed for this single lookup.
            IGuildUser? self = await guild.GetUserAsync(client.CurrentUser.Id, CacheMode.AllowDownload);
            if (self is null) { logger.LogWarning("bot: shared-role: self member not found in guild {Guild}", gid); return; }
            if (!BotRoles.NeedsRole(self.RoleIds, rid)) return;
            await self.AddRoleAsync(rid);
            logger.LogInformation("bot: shared-role: assigned {Role} in guild {Guild}", rid, gid);
        }
        catch (Exception ex) { logger.LogWarning(ex, "bot: shared-role: assign failed"); }
    }

    public async Task StopAsync(CancellationToken ct)
    {
        _stopping.Cancel();
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
