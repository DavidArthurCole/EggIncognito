# EggIncognito.Data

The optional Postgres layer (net10.0, EF Core 10 + Npgsql). References [EggIncognito.Core](../EggIncognito.Core/README.md).

Part of [EggIncognito](../README.md). Active only when `ConnectionStrings:Postgres` is set. With no connection string the app is file-only and the test suite runs DB-free.

## Entities

`EggIncognitoDbContext` plus these models (`Models/`):

| Entity | Table | Purpose |
|---|---|---|
| `StoredEndpoint` | `stored_endpoints` | A stored response that overrides the file default for a `(path, eid)`. |
| `StoredRoute` | `stored_routes` | Mirrors the yaml catalog (`source=yaml`) and holds user-added routes (`source=db`). |
| `User` | `users` | Discord identity, `discord_id` PK, carries `Role`. |
| `Doc` | `docs` | One Markdown body per subject. |
| `Tag` | `tags` | Slug/label/color catalog. |
| `SubjectTag` | `subject_tags` | Subject -> tag join. |
| `DocImage` | `doc_images` | Uploaded doc image bytes as Postgres bytea. |

`UserRole` (viewer / contributor / admin) is the role enum.

## Endpoint overlay

Lookup goes through the `IEndpointSource` seam.

- `FileEndpointSource` (the checked-in `Endpoints/` defaults) is always present.
- `DbEndpointSource` (`stored_endpoints`) is consulted first when a DB is configured, so a stored row overrides the file default for the same `(path, eid)`.
- The singleton `EndpointStore` resolves the scoped DB source per-lookup via a service scope. A transient DB error degrades to the file default, never a failed request.

## DB routes

- `StoredRoute` mirrors the compiled yaml catalog on boot (`RouteSeeder`, `source=yaml`) and holds user-added routes (`source=db`).
- `MergedRouteCatalog` keeps yaml authoritative. The DB only fills new paths.
- `DynamicMockController` (`[HttpPost("/{**slug}")]`, in the web project) serves DB-only routes at runtime. It resolves the response proto type by name and the body from the store, using the same base64 framing as the generated controllers. It only runs for paths no generated controller claimed.

## Write API

`/api/db/*` upserts stored endpoints/routes, gated by `IAppMode.CanWrite` (403 in Hosted; contributor+ for the role-gated actions). Reads (`GET /api/db/endpoints|routes`) are public. With no DB, writes return 503 and reads return `[]`.

## Migrations

- Auto-applied at startup when a DB is configured (`Database.MigrateAsync`). Fail fast on a broken DB.
- `DesignTimeDbContextFactory` lets `dotnet ef migrations add ... --project EggIncognito.Data` run without a live DB or the web host.
- Target DB: a new `eggincognito` database on the project's Postgres instance.
- Entities use `[Table]` / `[Column]` snake_case annotations.

This library also carries a `<FrameworkReference Include="Microsoft.AspNetCore.App"/>` so it can see the OAuth ticket type used by `UserUpsert.OnLoginAsync` (the Discord login upsert). See the web project's [Authentication](../EggIncognito/README.md#authentication-discord-oauth-optional) section.
