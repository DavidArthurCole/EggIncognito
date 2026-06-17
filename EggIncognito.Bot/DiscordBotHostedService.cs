using Discord;
using Discord.WebSocket;
using EggIncognito.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EggIncognito.Bot;

// Discord gateway connection for the bot's lifetime. Opt-in via Discord:BotToken.
// Connects in background with bounded retries. Never throws out of StartAsync.
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
                // Guilds (unprivileged) is required to cache guilds; without it guild interactions
                // can't resolve the invoker as a SocketGuildUser, so /updateserver's permission
                // check would fail closed for everyone.
                GatewayIntents = GatewayIntents.Guilds,
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

            // Boot with retries; Discord.Net owns reconnection after first connect. Never blocks StartAsync.
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

            // Register once; Ready re-fires on reconnect and re-pushing burns the global-command rate limit.
            if (_commandsRegistered) return;

            var commands = CommandDefinitions.BuildAll();
            var desired = CommandSignature.Compute(commands.Select(CommandSignature.FromProperties));

            // Skip the rate-limited bulk overwrite when Discord already holds this exact catalog.
            if (await GlobalCommandsMatchAsync(client, desired))
                logger.LogInformation("bot: global commands unchanged - skipping overwrite");
            else
                await client.BulkOverwriteGlobalApplicationCommandsAsync(commands);

            await EnsureGuildCommandsAsync(client, desired, commands);
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

    // With RegisterGuildCommands the catalog is mirrored to the guild for instant testing.
    // Otherwise guild-scoped commands are purged: a guild copy next to the global catalog shows
    // every command twice in the Discord UI.
    private async Task EnsureGuildCommandsAsync(DiscordSocketClient client, string desired, ApplicationCommandProperties[] commands)
    {
        if (string.IsNullOrWhiteSpace(options.GuildId) || !ulong.TryParse(options.GuildId, out var gid)) return;
        IGuild? guild = client.GetGuild(gid);
        if (guild is null) return;
        if (options.RegisterGuildCommands)
        {
            if (!await GuildCommandsMatchAsync(guild, desired))
                await guild.BulkOverwriteApplicationCommandsAsync(commands);
            return;
        }
        try
        {
            var existing = await guild.GetApplicationCommandsAsync();
            if (existing.Count == 0) return;
            await guild.BulkOverwriteApplicationCommandsAsync(Array.Empty<ApplicationCommandProperties>());
            logger.LogInformation("bot: purged {Count} guild-scoped command duplicates", existing.Count);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "bot: guild command purge failed");
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

    // Best-effort: grant SharedRoleId to the bot's own member. Any failure logs + returns. Idempotent.
    private async Task EnsureSharedRoleAsync(DiscordSocketClient client)
    {
        if (string.IsNullOrWhiteSpace(options.GuildId) || string.IsNullOrWhiteSpace(options.SharedRoleId)) return;
        if (!ulong.TryParse(options.GuildId, out var gid) || !ulong.TryParse(options.SharedRoleId, out var rid)) return;
        try
        {
            IGuild? guild = client.GetGuild(gid);
            if (guild is null) { logger.LogWarning("bot: shared-role: guild {Guild} not available", gid); return; }
            // CacheMode.AllowDownload falls back to REST; GuildMembers intent not needed for a single self-lookup.
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
