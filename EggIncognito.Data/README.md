# EggIncognito.Data

The optional Postgres layer (net10.0, EF Core 10 + Npgsql). References [EggIncognito.Core](../EggIncognito.Core/README.md).

Part of [EggIncognito](../README.md). Active only when `ConnectionStrings:Postgres` is set. With no connection string the app is file-only.

## Entities

`EggIncognitoDbContext` plus these models:

| Entity | Table | Purpose |
|---|---|---|
| `StoredEndpoint` | `stored_endpoints` | A stored response that overrides the file default for a `(path, eid)`. |
| `StoredRoute` | `stored_routes` | Mirrors the yaml catalog (`source=yaml`) and holds user-added routes (`source=db`). |
| `Doc` | `docs` | One Markdown body per subject. |
| `Tag` | `tags` | Slug/label/color catalog. |
| `SubjectTag` | `subject_tags` | Subject -> tag join. |
| `DocImage` | `doc_images` | Uploaded doc image bytes as Postgres bytea. |
| `ProtoVersion` | `proto_versions` | One game build in the registry, keyed by `(platform, build)`. Soft-delete + canonical-alias merge. |
| `ProtoProto` | `proto_protos` | The cleaned `.proto` text for one `ProtoVersion` plus a jsonb message/enum index. |
| `StagedProto` | `staged_protos` | A proto awaiting review (`status` pending/approved/rejected; `source` offer/crawl). |
| `FeedSubscription` | `feed_subscriptions` | A proto-update notification target (Discord webhook or HTTP). |
| `FeedDelivery` | `feed_deliveries` | One delivery attempt of a proto event to a subscription. |
| `KnownVersion` | `known_versions` | A version seen in the wild by a list source (metadata only, no proto). |
| `BackfillJob` | `backfill_jobs` | Progress of a backfill import run. |
| `ExtractJob` | `extract_jobs` | Progress of one per-version APK extract run. |
| `Device` | `devices` | A physical device this host can probe (config-seeded). |
| `DeviceProbe` | `device_probes` | One append-only probe of a device's installed version. |
| `DeviceUpdate` | `device_updates` | One append-only update attempt on a device. |
| `CaptureProxyAddr` | `capture_proxy_addrs` | A user's stable IPv6 hosted-capture address. |
| `CaptureUserCa` | `capture_user_cas` | A user's hosted-capture root CA pfx (bytea). |
| `ApiKey` | `api_keys` | A user-minted API key: SHA-256 hash + 12-char prefix, owner user id, usage counters, revocation. |
| `StoredMesh` | `stored_meshes` | A device-pulled `.glb` mesh cached in the DB, keyed by `(platform, stem)`. |
| `StoredIcon` | `stored_icons` | A device-pulled icon PNG cached in the DB, keyed by asset name. |
| `EnvDesign` | `env_designs` | A saved playground farm-layout design. |
| `EnvDesignVersion` | `env_design_versions` | One version snapshot of an `EnvDesign`. |

`UserRole` (viewer / contributor / admin) is the role enum. Users themselves live in the external
EggIdentity service, not in this database. Roles ride in the auth cookie's `egi:role` claim; an
owner user id on a row is the identity `Guid` with no local foreign key.

## Endpoint overlay

Lookup goes through the `IEndpointSource` seam.

- `FileEndpointSource` (the checked-in file defaults) is always present.
- `DbEndpointSource` (`stored_endpoints`) is consulted first when a DB is configured, so a stored row overrides the file default for the same `(path, eid)`.
- The singleton `EndpointStore` resolves the scoped DB source per-lookup via a service scope. A transient DB error degrades to the file default, never a failed request.

## DB routes

- `StoredRoute` mirrors the compiled yaml catalog on boot (`RouteSeeder`, `source=yaml`) and holds user-added routes (`source=db`).
- `MergedRouteCatalog` keeps yaml authoritative. The DB only fills new paths.
- `DynamicMockController` (`[HttpPost("/{**slug}")]`, in the web project) serves DB-only routes at runtime. It resolves the response proto type by name and the body from the store, using the same base64 framing as the generated controllers. It only runs for paths no generated controller claimed.

## Write API

`/api/db/*` upserts stored endpoints/routes, gated by `IAppMode.CanWrite` (403 in Hosted; contributor+ for the role-gated actions). Reads (`GET /api/db/endpoints|routes`) are public. With no DB, writes return 503 and reads return `[]`.

## Proto-version registry

A versioned catalog of game proto definitions per platform.

- `ProtoVersion` is the build row, keyed by `(platform, build)`. It carries three version numbers: `app_version` (user label), `build` (monotonic versionCode), `client_version` (proto API version, nullable).
- `ProtoProto` holds the cleaned `.proto` text plus a jsonb `message_index` for fast listing/search.
- Soft-delete (`deleted_at`) hides a row without physically removing it, so an auto-importer cannot resurrect it. A merge (`canonical_id`) makes a row an alias of a canonical build sharing the same schema. Both are reversible.
- `KnownVersion` is a metadata-only version list read by the device farm's `StoreAheadCheck` (is the device's store ahead of the installed build?). The automated external-source populators (Fandom/elgranjero/app-store scrapers) were removed, so it is only written by device-store checks now.
- `BackfillJob` and `ExtractJob` are dormant legacy tables from the removed automated-backfill subsystem; nothing writes them anymore.

## Staged protos

A review queue in front of the live registry.

- `StagedProto.source` is `offer` (a user analyzed a binary whose proto is not yet stored) or `crawl` (admin import of the GitHub backfill dataset).
- `status` is `pending` / `approved` / `rejected`. Approval promotes the row into `proto_versions`.

## Proto feed

Outbound notifications when a proto changes or a new build lands.

- `FeedSubscription` is a Discord webhook or HTTP target with chosen `platforms` + `trigger` (`proto_changed` / `new_version`). The target URL is the capability and is never echoed back.
- `FeedDelivery` records one attempt per `(subscription, proto_version)`, so a retried ingest delivers at most once.

## Device farm

Status and update history for the physical devices this host probes.

- `Device` rows are seeded from config on boot (config is authoritative); a dropped device is disabled, not deleted, to keep history FKs valid.
- `DeviceProbe` is append-only: each row records reachability and the installed version with a `result` (`no_change` / `new_version` / `unreachable` / `error`).
- `DeviceUpdate` is append-only update history with a `status` (`verified` / `failed` / `skipped`).

## Hosted capture

- `CaptureProxyAddr` is a user's stable per-user IPv6 proxy address, rotatable to kill a leaked one.
- `CaptureUserCa` is the user's Unobtanium root CA pfx, restored to the session temp dir at start so the device trusts one cert forever.

## Migrations

- Auto-applied at startup when a DB is configured (`Database.MigrateAsync`). Fail fast on a broken DB.
- A design-time context factory lets `dotnet ef migrations add` run without a live DB or the web host.
- Target DB: a new `eggincognito` database on the project's Postgres instance.
- Entities use `[Table]` / `[Column]` snake_case annotations.

This library references `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore`, which persists the app's data-protection keys (auth-cookie signing keys) in the DB so cookie logins survive restarts (`data_protection_keys` table). It also references `EggIdentity.Client` for the revocation-check call on cookie validation. Auth itself is the external EggIdentity service. See the web project's [Authentication](../EggIncognito/README.md#authentication-eggidentity-optional) section.
