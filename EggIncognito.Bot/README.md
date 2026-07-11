# EggIncognito.Bot

The optional Discord bot (net10.0, Discord.Net). References [EggIncognito.Core](../EggIncognito.Core/README.md) + [EggIncognito.Capture](../EggIncognito.Capture/README.md).

Part of [EggIncognito](../README.md). Opt-in Discord bot backed by SyncKit.Bot.

## How it works

- Opt-in via `Discord:BotToken`. With no token the bot never starts.
- `Program.cs` builds one `SyncKit.Bot.BotConfig` (global commands, `Extra` = health/status/endpoints/proto)
  and hands it to `SyncKitBot.StartAsync`, wrapped by `EggIncognitoBotHostedService : IHostedService`.
- Gateway lifecycle, command registration/diffing, presence, shared-role assignment, and the
  `/verify`+`/updateserver` builtins all live in `SyncKit.Bot`. This project only supplies domain
  commands and their embeds.
- A bot failure never crashes the host.

## Slash commands

Six global, user-installable commands (four domain, two builtins) plus one guild-admin builtin:

| Command | Source | Purpose |
|---|---|---|
| `/health` | domain | Liveness. |
| `/status` | domain | App status snapshot. |
| `/verify` | builtin | Surfaces the running git SHA. |
| `/endpoints` | domain | Endpoint listing. |
| `/proto` | domain | Proto type list + lookup with autocomplete. |
| `/updateserver` | builtin | Pull latest and redeploy (guild-admin only). |

## Structure

- `ExtraCommands.cs`: the four domain `SyncKit.Bot.BotCommand` factories (definitions + handlers), including `/proto`'s autocomplete.
- `EggIncognitoBotHostedService.cs`: thin `IHostedService` wrapper around `SyncKitBot.StartAsync`/`DisposeAsync`; also populates `BotInstanceHolder.Bot` on successful start.
- `BotInstanceHolder.cs`: singleton holding the live `SyncKitBot` instance (or null), read by `/bot-admin` route handlers at request time since routes are mapped before hosted services start.
- `BotAdminRoutes.cs`: vendored from `SyncKit.Bot.AdminRoutes`, paths remapped `/admin` -> `/bot-admin` (the bare path collides with this app's own Blazor admin console).
- `CommandParsing.cs`, `ProtoQuery.cs`, `BotEmbeds.cs`, `IStatusProvider.cs`, `StatusSnapshot.cs`: unchanged pure/domain logic.
- `IStatusProvider` is implemented by `StatusSnapshotFactory` in the web project, so this library stays decoupled from `IAppMode`.

## Operator setup

- Register the bot token as `Discord:BotToken` (env / appsettings / user-secrets).
- Optionally set `Discord:GuildId` for instant guild command registration during development. Global commands propagate more slowly.
- `Discord:ClientId` doubles as the bot's app id.
- `/verify` is a `SyncKitBot` builtin. `/updateserver` is also a builtin (guild-admin only, POSTs to host-side deploy agent via `DEPLOY_AGENT_URL`/`DEPLOY_AGENT_SECRET`).

## Bot admin dashboard (`/bot-admin`)

Discord-OAuth-gated web UI for editing the deploy-notification config (dashboard channel, enabled
threads, message templates) that `SyncKit.Bot` uses to post to Discord on new-version events.

- Opt-in: needs `ConnectionStrings:Postgres` set plus all three `Discord:BotAdminClientId` /
  `Discord:BotAdminClientSecret` / `Discord:BotAdminCallbackUrl`. Missing any of these (or no bot
  token) leaves `/bot-admin` unmapped, 404.
- Login is guild-admin only, checked against `BotConfig.GuildId` via the live `SyncKitBot.Client`
  (through `BotInstanceHolder`), so it 503s until the bot has finished starting.
- `BotConfig.PostgresConnectionString`/`DashboardChannelId`/`EnabledThreads` (set from `pgConn` and
  `Discord:DashboardChannelId`/`Discord:EnabledThreads`) feed `SyncKit.Bot`'s own dashboard notifier;
  the `/bot-admin` UI is how an operator edits those last two without a redeploy.
- `Migrations/*.up.sql`: vendored SQL applied via `SyncKit.Db.Migrator.MigrateAsync` at startup,
  copied to output so the runtime path resolves without a source checkout.
