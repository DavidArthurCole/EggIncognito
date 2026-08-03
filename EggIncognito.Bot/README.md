# EggIncognito.Bot

The optional Discord bot (Discord.Net, backed by EggIdentity.Bot). Part of [EggIncognito](../README.md).

Opt-in via `Discord:BotToken`; with no token it never starts, and a bot failure never crashes the host. Gateway lifecycle, command registration, and the builtin commands live in EggIdentity.Bot; this project supplies the domain slash commands (health, status, endpoint listing, proto lookup with autocomplete) and their embeds.

`/bot-admin` is the web UI for the bot's deploy-notification config. It needs both Postgres and a bot token, and rides the app's own admin login rather than a separate Discord OAuth flow.
