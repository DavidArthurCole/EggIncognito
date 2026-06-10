# EggIncognito.Bot

The optional Discord bot (net10.0, Discord.Net). References [EggIncognito.Core](../EggIncognito.Core/README.md) + [EggIncognito.Capture](../EggIncognito.Capture/README.md).

Part of [EggIncognito](../README.md). A gateway bot wired as `DiscordBotHostedService : IHostedService`. Opt-in.

## How it works

- Opt-in via `Discord:BotToken`. With no token the bot never starts.
- It reuses `Discord:ClientId` as the app id. Optional `Discord:GuildId` enables instant guild command registration.
- Static "Playing EggIncognito" presence.
- A bot failure never crashes the host.

## Slash commands

Five global + user-installable read-only commands:

| Command | Purpose |
|---|---|
| `/health` | Liveness. |
| `/status` | App status snapshot. |
| `/verify` | Surfaces the running git SHA. |
| `/endpoints` | Endpoint listing. |
| `/proto` | Proto type list + lookup with autocomplete. |

## Structure

- Pure, unit-tested logic (`BotEmbeds`, `ProtoQuery`, `CommandDefinitions`) sits behind thin Discord glue (`InteractionRouter`, `DiscordBotHostedService`).
- `IStatusProvider` (Bot) is implemented by `StatusSnapshotFactory` in the web project, so the Bot library stays decoupled from `IAppMode`.
- `/verify` surfaces the git SHA stamped into the assembly `InformationalVersion` by the `SourceRevisionId` target in the repo-root `Directory.Build.props`, parsed by `BuildInfo` in Core.

## Operator setup

- Register the bot token as `Discord:BotToken` (env / appsettings / user-secrets).
- Optionally set `Discord:GuildId` for instant guild command registration during development. Global commands propagate more slowly.
- `Discord:ClientId` doubles as the bot's app id.
