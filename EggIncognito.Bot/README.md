# EggIncognito.Bot

The optional Discord bot (net10.0, Discord.Net). References [EggIncognito.Core](../EggIncognito.Core/README.md) + [EggIncognito.Capture](../EggIncognito.Capture/README.md).

Part of [EggIncognito](../README.md). A gateway bot wired as `DiscordBotHostedService : IHostedService`. Opt-in.

## How it works

- Opt-in via `Discord:BotToken`. With no token the bot never starts.
- It reuses `Discord:ClientId` as the app id. Optional `Discord:GuildId` enables instant guild command registration.
- Static "Playing EggIncognito" presence.
- A bot failure never crashes the host.

## Slash commands

Five global, user-installable, read-only commands plus one guild-admin command:

| Command | Scope | Purpose |
|---|---|---|
| `/health` | read-only | Liveness. |
| `/status` | read-only | App status snapshot. |
| `/verify` | read-only | Surfaces the running git SHA. |
| `/endpoints` | read-only | Endpoint listing. |
| `/proto` | read-only | Proto type list + lookup with autocomplete. |
| `/updateserver` | guild admin | Pull latest and redeploy. |

### `/updateserver`

- Registered with `GuildPermission.Administrator`, so it only appears in a guild context.
- `InteractionRouter` re-checks `GuildPermissions.Administrator` at runtime (defense-in-depth against rebound permissions); a non-admin gets an ephemeral "Not authorized."
- On a real run it POSTs to a host-side deploy agent via `DeployAgentClient`, configured by `Discord:DeployAgentUrl` + `Discord:DeployAgentSecret`. Either missing means the command replies "Deploy agent not configured."
- Result embeds cover already-up-to-date, success (from/to hash), and failure (log tail).

## Structure

- Pure, unit-tested logic (`BotEmbeds`, `ProtoQuery`, `CommandDefinitions`, `CommandParsing`, `CommandSignature`) sits behind thin Discord glue (`InteractionRouter`, `DiscordBotHostedService`).
- `IStatusProvider` (Bot) is implemented by `StatusSnapshotFactory` in the web project, so the Bot library stays decoupled from `IAppMode`.
- `/verify` surfaces the git SHA stamped into the assembly `InformationalVersion` by the `SourceRevisionId` target in the repo-root `Directory.Build.props`, parsed by `BuildInfo` in Core.

## Operator setup

- Register the bot token as `Discord:BotToken` (env / appsettings / user-secrets).
- Optionally set `Discord:GuildId` for instant guild command registration during development. Global commands propagate more slowly.
- `Discord:ClientId` doubles as the bot's app id.
