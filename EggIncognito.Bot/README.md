# EggIncognito.Bot

The optional Discord bot (net10.0, Discord.Net). References [EggIncognito.Core](../EggIncognito.Core/README.md) + [EggIncognito.Capture](../EggIncognito.Capture/README.md).

Part of [EggIncognito](../README.md). Opt-in Discord bot backed by SyncKit.Bot.

## How it works

- Opt-in via `Discord:BotToken`. With no token the bot never starts.
- Startup builds one `SyncKit.Bot.BotConfig` (global commands, `Extra` = health/status/endpoints/proto)
  and hands it to `SyncKitBot.StartAsync`, wrapped by a hosted-service wrapper implementing `IHostedService`.
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

- The domain command factories: the four `SyncKit.Bot.BotCommand` definitions and handlers, including `/proto`'s autocomplete.
- The hosted-service wrapper: thin `IHostedService` around `SyncKitBot.StartAsync`/`DisposeAsync`.
- The vendored admin routes: from `SyncKit.Bot.AdminRoutes`, paths remapped `/admin` -> `/bot-admin` (the bare path collides with this app's own Blazor admin console). Takes an `isAdmin` delegate instead of running its own Discord OAuth flow, since this project can't reference the web project's `ICurrentUser` directly.
- Command parsing, proto query, embeds, and the status-provider abstraction (`IStatusProvider`) are pure domain logic.
- `IStatusProvider` is implemented by `StatusSnapshotFactory` in the web project, so this library stays decoupled from `IAppMode`.

## Operator setup

- Register the bot token as `Discord:BotToken` (env / appsettings / user-secrets).
- Optionally set `Discord:GuildId` for instant guild command registration during development. Global commands propagate more slowly.
- `Discord:ClientId` doubles as the bot's app id.
- `/verify` is a `SyncKitBot` builtin. `/updateserver` is also a builtin (guild-admin only, POSTs to host-side deploy agent via `DEPLOY_AGENT_URL`/`DEPLOY_AGENT_SECRET`).

## Bot admin dashboard (`/bot-admin`)

Web UI for editing the deploy-notification config (dashboard channel, enabled threads, message
templates) that `SyncKit.Bot` uses to post to Discord on new-version events.

- Opt-in: needs `ConnectionStrings:Postgres` set plus a bot token. Missing either leaves
  `/bot-admin` unmapped, 404.
- Auth rides this app's own centralized login (`ICurrentUser`/`UserRole.Admin`, the same gate as
  the app's existing `/admin` console), not a separate Discord OAuth flow. Startup passes the
  check in as a delegate since `EggIncognito.Bot` can't reference `ICurrentUser` (would be circular).
- `BotConfig.PostgresConnectionString`/`DashboardChannelId`/`EnabledThreads` (set from `pgConn` and
  `Discord:DashboardChannelId`/`Discord:EnabledThreads`) feed `SyncKit.Bot`'s own dashboard notifier;
  the `/bot-admin` UI is how an operator edits those last two without a redeploy.
- The migration SQL: vendored files applied via `SyncKit.Db.Migrator.MigrateAsync` at startup,
  copied to output so the runtime path resolves without a source checkout. Runs whenever Postgres +
  a bot token are configured, independent of the `/bot-admin` UI's own gate (`SyncKitBot`'s channel
  hub touches these tables regardless of whether the config UI is enabled).
