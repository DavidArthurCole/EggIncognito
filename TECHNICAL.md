Internals and reference for [EggIncognito](README.md). For the capture walkthrough see [CAPTURE.md](CAPTURE.md).

## Wire format

- Request: `POST /<path>`. Body is `data=<base64(proto bytes)>`. Content type `application/x-www-form-urlencoded`.
- Response: base64-encoded proto bytes. Content-Type is `text/html` for success (2xx) and `text/plain` for errors.
- The real Egg, Inc. API wraps many responses in an `AuthenticatedMessage` envelope, optionally gzip/zlib/deflate compressed. The mock returns the inner response message directly.
- Signed requests wrap the inner proto in an `AuthenticatedMessage` with a SHA256-based `code`.

## Architecture

| Project | Purpose |
|---|---|
| `EggIncognito` | ASP.NET Core (net10.0) - the single app: mock controllers, the Inspector (`/inspector/`), capture (`/capture/`), and Import (`/import/`) SPAs + their controllers, the read-only Tools API (`/api/tools/*`) and the write Import API (`/api/import/*`), and `IAppMode` (Local/Hosted gating). |
| `EggIncognito.Core` | Class lib (net10.0) - the shared library code (`Services/EndpointExtractor.cs`, `Services/EndpointStore.cs`, `Services/TransportPipeline.cs`, `Services/RouteCatalog.cs`, `Services/AuxbrainHosts.cs`, etc.) plus the `Ei.*` proto types (`Proto/ei.proto` built by Grpc.Tools). No web dependency. |
| `EggIncognito.Capture` | Class lib (net10.0) - the capture engine: `UnobtaniumCaptureProxy` (selective-decrypt of auxbrain only), `CaptureHub`/`FlowProcessor`/`FlowDecoder`/`HarWriter`, and `CaptureSession` (start/stop lifecycle owner). Refs Core. See [CAPTURE.md](CAPTURE.md). |
| `EggIncognito.Data` | Class lib (net10.0) - the optional Postgres layer (EF Core 10 + Npgsql): `EggIncognitoDbContext`, `StoredEndpoint`/`StoredRoute` entities, `Migrations/`, the DB-backed `IEndpointSource`/`IDbRouteProvider` impls, and `RouteSeeder`. Active only when `ConnectionStrings:Postgres` is set. Refs Core. |
| `EggIncognito.RouteGenerator` | Roslyn `IIncrementalGenerator` (netstandard2.0) - reads `routes.yaml` and emits one controller per endpoint at build time. |
| `EggIncognito.Bot` | Class lib (net10.0) - the optional Discord bot (Discord.Net). Refs Core + Capture. `DiscordBotHostedService` (opt-in via `Discord:BotToken`), pure `BotEmbeds`/`ProtoQuery`/`CommandDefinitions`, the `IStatusProvider` seam (impl'd by `StatusSnapshotFactory` in the web project). |
| `EggIncognito.Tests` | xUnit - unit tests for the generator, `EndpointStore`, the extraction pipeline, capture, the Core tooling services (`PostmanCollection`/`EndpointStatus`/`BlobDecoder`/`ContentRoot`/`AppMode`), and `WebApplicationFactory` integration tests (incl. the Hosted-mode gate). |

Controllers are generated into `obj/` at build time. They are not checked in. Never edit generated files. To add an endpoint, edit `routes.yaml` only:

```yaml
- path: ei/my_endpoint
  request: MyRequestType
  response: MyResponseType
```

Then run `dotnet build`. Add an endpoint file under `EggIncognito/Endpoints/default/<namespace>/` if a non-default response is needed.

The extraction pipeline lives in `EggIncognito.Core/Services/EndpointExtractor.cs`. It exposes one per-flow method, `ProcessFlow(url, method, status, requestDataB64, responseBodyB64)`. Two paths funnel through it, so they cannot diverge:

- The HAR import path (`/api/import/har` -> `RunFromHar`).
- The in-process capture path (the in-app capture proxy, driven by `CaptureSession`).

## Distribution

The app ships two ways. Both are produced by the `Release` workflow on a `v*` tag.

- **Self-contained binaries** - `dotnet publish --self-contained -p:PublishSingleFile=true` per runtime: `linux-x64`, `linux-arm64`, `osx-arm64`, `win-x64`. No .NET install needed. The `Endpoints/` response set is bundled alongside the executable. Attached to the GitHub release.
- **Container image** - `ghcr.io/davidarthurcole/eggincognito:<tag>` and `:latest`, built from the repo `Dockerfile`.

To build a single-file binary locally:

```sh
dotnet publish EggIncognito/EggIncognito.csproj -c Release -r linux-x64 \
  --self-contained true -p:PublishSingleFile=true -o ./publish
cp -r EggIncognito/Endpoints ./publish/Endpoints
```

## Configuration

| Variable | Default | Description |
|---|---|---|
| `EndpointsPath` | `<app dir>/Endpoints` | Endpoints (response payloads) root |
| `ASPNETCORE_URLS` | `http://+:8080` | Bind address (used in Docker, overridden when certs present) |
| `CertsPath` | `<app dir>/certs` | Directory containing `server.crt` and `server.key` |
| `HttpPort` | `8080` | HTTP port when certs are present (overrides ASPNETCORE_URLS) |
| `HttpsPort` | `8443` | HTTPS port (only active when certs are present) |
| `AppMode` | `Local` | `Local` = full features; `Hosted` = capture + writes disabled (the public deploy) |
| `ConnectionStrings:Postgres` | unset | When set, enables the Postgres data layer (overlay + DB routes) and applies migrations at startup. Unset = file-only. |
| `Discord:ClientId` / `Discord:ClientSecret` | unset | Discord OAuth app credentials. Auth wires only when both are set AND a DB is configured. |
| `Discord:AdminIds` | unset | Comma-separated Discord ids auto-promoted to admin on login (bootstraps the first admin). |
| `CaptureEnabled` | follows mode | Per-capability override for the capture proxy |
| `WritesEnabled` | follows mode | Per-capability override for endpoint writes (HAR import + status rewrite) |
| `ContentRoot` | auto-resolved | The dir holding `RouteMap/` + `Endpoints/` (resolved from the app/base dir; rarely set) |
| `EGG_INC_EID` | optional | EID used to scrub captured/imported data and as the in-app capture default |
| `CapturePort` | `8080` | Port the capture proxy listens on |
| `CapturePath` | `<content root>/captures` | Directory the capture HAR is written to |
| `CaPath` | `<CapturePath>/eggincognito-ca.cer` | The persisted capture root CA file |
| `EGG_INC_API_SALT` | required for request signing | API signing phrase for the Inspector's "Real auxbrain" sends |

## Run modes

`IAppMode` (`Services/AppMode.cs`) splits the app into two modes via the `AppMode` config key.

- **Local** (default) - full features. Capture and endpoint writes mutate the local `Endpoints/`. This is the developer / self-host mode.
- **Hosted** (`AppMode=Hosted`) - the public deploy (`https://eggincognito.davidarthurcole.me`). A request must not mutate shared data, and the proxy/CA cannot be shared. So capture (`/api/capture/start`, `save-endpoint`) and writes (`/api/import/*`) return 403. The mock API, Inspector, and read-only Tools stay fully available. `CaptureEnabled` / `WritesEnabled` override per capability.

The UI reads `GET /api/app/mode`. It hides the Capture/Import nav links when those capabilities are off (`wwwroot/nav.js`).

## Web tooling

The former PowerShell scripts and CLI subcommands are gone. Their behavior is now web endpoints or build steps. The proto-refresh, APK-diff, mitmproxy-extract, and publish scripts have been removed.

| Where | Purpose |
|---|---|
| Inspector Tools -> `GET /api/tools/postman-collection` | Download a Postman v2.1 collection from `routes.yaml` (was `export-collection`) |
| Inspector Tools -> `GET /api/tools/endpoint-status` | Report routes with a missing or empty endpoint (was `check-endpoints`) |
| `POST /api/import/endpoint-status/update` (Local) | Rewrite the `endpoint_status:` block (was `check-endpoints --update`) |
| Inspector Tools -> `POST /api/tools/decode` | Auto-detect the proto type of an arbitrary base64 blob (was `decode`) |
| Import tab -> `POST /api/import/har` (Local) | Import a HAR into `Endpoints/default/` (staged unless `?overwrite=true`) (was `from-har`) |
| `EmitDashboardTypes` MSBuild target | Regenerate `wwwroot/capture/types.d.ts` from the capture records on every build (was `emit-types`) |
| `BuildTailwind` MSBuild target | Compile `wwwroot/app.tailwind.css` -> `wwwroot/tailwind.css` with the standalone Tailwind CLI. See notes below. |

`BuildTailwind` details:

- No Node. The standalone Tailwind CLI is fetched to `tools/tailwind/` on first build (gitignored).
- Tokens live only in `tailwind.config.js`. All chrome is one canonical `@layer components` block in `app.tailwind.css`.
- Phase 2 migrated Import. Phase 3 unified Inspector + Capture onto the shared layer and deleted both per-tab `styles.css`. Admin + landing still use small inline `<style>`.
- `.btn-primary` is orange (`--accent`) everywhere. Blue (`--accent2`) is info/secondary.
- Inspector/Capture are JS-rendered. The migration defines the semantic class names the JS already emits, so there is no JS rewrite. Add a missing class to the component layer; never rename in JS.
- Skip with `-p:BuildTailwindCss=false`.
- Gotcha: the target runs `AfterTargets="Build"`, so a wwwroot-only incremental build does not recompile. Run `dotnet build EggIncognito/EggIncognito.csproj -t:BuildTailwind`.
- `@apply hidden` inside `@layer components` is a circular-dependency error. Use raw `display:none`.
- The fetch is RID-aware (right asset per OS + arch), so Linux/macOS CI compiles the sheet natively.

The live-API batch seed (former `seed` subcommand) is not yet web-ported. Recover it from git history if a Local seeding flow is needed.

### Inspector send targets

The Inspector's Send toggle has three targets.

- **Mock** - posts to this instance's own mock API (`location.origin`). Always available.
- **Live API** - the server's `/api/inspector/send` egresses to auxbrain. When `AppMode=Hosted` this is login-gated: an anonymous request gets 403. The server only egresses for authenticated users. This is the seam the future per-user-proxy tier refines. Local is unrestricted. The SPA greys out the toggle for anonymous hosted users by reading `GET /api/app/mode`.
- **Custom** - browser-direct. The browser POSTs the prebuilt form body straight to the user's own proxy URL (`inspector.customTarget` in localStorage). This server is fully bypassed, with zero egress. The bytes the browser receives are decoded by `POST /api/inspector/decode-response`, an egress-free pure-decode helper (proto reflection only, no network, no salt). The custom fetch is CORS-bound to whatever the user's proxy allows.

The signing salt model is unchanged. It stays client-owned (`inspector.salt`, sent only in `/build`). The server is never a signing oracle. `EGG_INC_API_SALT` is never read on the Inspector path.

## Data layer (Postgres, optional)

`EggIncognito.Data` adds an optional Postgres overlay (EF Core 10 + Npgsql), active only when `ConnectionStrings:Postgres` is set. With no connection string the app is file-only and the test suite runs DB-free.

- **Endpoint overlay.** Lookup goes through the `IEndpointSource` seam. `FileEndpointSource` (the checked-in `Endpoints/` defaults) is always present. `DbEndpointSource` (`stored_endpoints`) is consulted first when a DB is configured, so a stored row overrides the file default for the same `(path, eid)`. The singleton `EndpointStore` resolves the scoped DB source per-lookup via a service scope. A transient DB error degrades to the file default.
- **DB routes.** `StoredRoute` mirrors the compiled yaml catalog on boot (`RouteSeeder`, `source=yaml`) and holds user-added routes (`source=db`). `MergedRouteCatalog` keeps yaml authoritative; the DB only fills new paths. `DynamicMockController` (`[HttpPost("/{**slug}")]`) serves DB-only routes at runtime, resolving the response proto type by name and the body from the store. It uses the same base64 framing as the generated controllers. It only runs for paths no generated controller claimed.
- **Write API.** `/api/db/*` upserts stored endpoints/routes, gated by `IAppMode.CanWrite` (403 in Hosted). Reads (`GET /api/db/endpoints|routes`) are public. With no DB, writes return 503 and reads return `[]`.
- **Migrations** auto-apply at startup when a DB is configured (`Database.MigrateAsync`). `DesignTimeDbContextFactory` lets `dotnet ef migrations add ... --project EggIncognito.Data` run without a live DB. Target DB: a new `eggincognito` database on the same Postgres instance.

## Authentication (Discord OAuth, optional)

Cookie auth + `AspNet.Security.OAuth.Discord`, wired only when a DB AND `Discord:ClientId` + `Discord:ClientSecret` are configured (`AuthSetup.AddDiscordAuthIfConfigured`). Without that config the app is fully anonymous and unchanged.

- **Flow.** `GET /login` issues a Discord challenge. The middleware handles the `/discord-auth` callback, issues the cookie, and runs `UserUpsert.OnLoginAsync` (upserts the `users` row). `POST /logout` clears the cookie. `GET /api/auth/me` + `GET /api/app/mode` report the current user. The SPA renders login state from `/api/app/mode` (`authEnabled` + `user`).
- **Identity.** `User` entity in `EggIncognito.Data` (`discord_id` PK). `ICurrentUser` (web) exposes the Discord claims server-side. `AuthState` is a singleton flag for the wired/not-wired branch. Login gates nothing in this phase (Phase 3 adds write ACLs).
- **Operator setup.** Register the OAuth app's redirect URI in the Discord developer portal: `https://eggincognito.davidarthurcole.me/discord-auth` (prod) and `http://localhost:5032/discord-auth` (local dev). Scope: `identify` (no email).

## Discord bot (optional)

A Discord.Net gateway bot (`EggIncognito.Bot`), wired as `DiscordBotHostedService : IHostedService`. Opt-in via `Discord:BotToken`. It reuses `Discord:ClientId` as the app id; optional `Discord:GuildId` enables instant guild command registration. Static "Playing EggIncognito" presence.

- Global + user-installable read-only slash commands: `/health`, `/status`, `/verify`, `/endpoints`, `/proto` (list + type lookup w/ autocomplete).
- Pure, unit-tested logic (`BotEmbeds`, `ProtoQuery`, `CommandDefinitions`) sits behind thin Discord glue (`InteractionRouter`, the hosted service).
- `IStatusProvider` (Bot) is implemented by `StatusSnapshotFactory` (web), so the Bot lib stays decoupled from `IAppMode`.
- A bot failure never crashes the host.
- `/verify` surfaces the git SHA stamped into the assembly `InformationalVersion` by the `SourceRevisionId` target in the repo-root `Directory.Build.props`, parsed by `BuildInfo` (Core).

## Authorization (roles + ACLs)

`User.Role` (viewer / contributor / admin) is resolved on login and stamped into the cookie as the `egi:role` claim. There are two orthogonal authorities:

- `IAppMode.CanWrite` gates local file ops (`/api/import/*`, capture). Unchanged.
- The role gate authorizes shared-DB writes: `/api/db/endpoint` + `/api/db/route` require contributor+ (403 otherwise). Reads stay public. This replaces the old `CanWrite` check on those actions.

The first admin is bootstrapped from `Discord:AdminIds`. New users default to viewer. `/api/admin/*` (admin-only) lists/promotes users and reviews/deletes DB contributions. `/admin/` is the admin SPA, with its nav link shown only to admins. Viewers/anonymous who try to save get a browser-`localStorage` fallback. The role check + admin self-lockout guard run before any DB access, so they are testable DB-free.

## Rate limiting

Built-in `Microsoft.AspNetCore.RateLimiting` (`Services/RateLimiting/`). `UseRateLimiter()` is placed after auth (the partition key needs the authenticated user) and before `MapControllers`.

- **Partitioning.** Per caller: `user:{DiscordId}` when logged in, else `ip:{clientIp}` from `CF-Connecting-IP`. Cloudflare is the sole prod ingress. The IP falls back to `X-Forwarded-For` first hop, then the socket IP.
- **Tiers.** anon (30/min) < viewer (120/min) < contributor+ (600/min).
- **Named policies.** `egress` (Inspector `send`, 10/min), `write` (import/db/docs/admin), `read` (inspector/tools), plus a `Global` 300/min backstop over everything (incl. mock routes).
- **Effective permit** = `min(policy, tier)`.
- **Rejections.** 429 + `Retry-After` + a small JSON body.
- **Config.** Driven by the `RateLimiting` appsettings section over `RateLimitOptions.Defaults()`. `Enabled:false` makes it a no-op. `RateLimitOptions`/`RateLimitKeys` are pure + unit-tested.
- **Security note.** Trusting `CF-Connecting-IP` is safe only while Cloudflare is the only path to the origin.
