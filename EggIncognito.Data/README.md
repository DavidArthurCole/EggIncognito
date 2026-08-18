# EggIncognito.Data

The optional Postgres layer (EF Core + Npgsql). Active only when a Postgres connection string is set; without one the app is file-only. Migrations auto-apply at startup. Part of [EggIncognito](../README.md).

What it stores:

- Endpoint overlay: stored responses that override the file defaults per `(path, eid)`, plus user-added routes. A transient DB error degrades to the file default, never a failed request.
- Docs: per-subject Markdown, tags, and images.
- Proto registry: one row per game build with its cleaned proto text, soft-delete and canonical-alias merge, a staged-review queue in front of it, and outbound feed subscriptions with per-delivery dedup.
- Device farm: config-seeded device rows with a single append-only job timeline per device, the harvested device assets, and the stored device app binaries.
- Hosted capture: per-user proxy addresses and root CAs.
- API keys: hashed user-minted keys.
- Themes: per-user theme models and the site-wide theme policy.
- Playground: saved farm-layout designs with version snapshots.

Users are not stored here; they live in the external EggIdentity service, and row ownership is the identity id with no local foreign key. Auth-cookie data-protection keys persist in the DB so logins survive restarts.
