# EggIncognito (web app)

The single ASP.NET Core app (net10.0). It serves the mock Egg, Inc. API, the Transport Inspector, the live Capture dashboard, the Import tab, and the Admin tab. It also hosts the read-only Tools API and the write Import/DB APIs.

Part of [EggIncognito](../README.md). Sibling projects:

- [EggIncognito.Core](../EggIncognito.Core/README.md) - shared services + proto types.
- [EggIncognito.Capture](../EggIncognito.Capture/README.md) - the capture engine.
- [EggIncognito.Data](../EggIncognito.Data/README.md) - optional Postgres layer.
- [EggIncognito.Bot](../EggIncognito.Bot/README.md) - optional Discord bot.
- [EggIncognito.RouteGenerator](../EggIncognito.RouteGenerator/README.md) - the controller source generator.

## Wire format

- Request: `POST /<path>`. Body is `data=<base64(proto bytes)>`. Content type `application/x-www-form-urlencoded`.
- Response: base64-encoded proto bytes. Content-Type is `text/html` for success (2xx) and `text/plain` for errors.
- The real Egg, Inc. API wraps many responses in an `AuthenticatedMessage` envelope, optionally gzip/zlib/deflate compressed. The mock returns the inner response message directly.
- Signed requests wrap the inner proto in an `AuthenticatedMessage` with a SHA256-based `code`.

## Frontend

The UI is Blazor Server (Razor Components in `Components/`). `Components/App.razor` is the host page; `Components/Pages/*.razor` are the routable pages over `Components/Layout/MainLayout.razor` + `TopNav.razor`. `Components/Shared/` holds reusable pieces (`Icon`, `Markdown`, `MarkdownEditor`, `DocHelp`). Interactivity is `InteractiveServer` over a SignalR circuit.

- Pages: `/` (Home), `/inspector`, `/capture`, `/import`, `/protos`, `/docs`, `/admin`, `/support`.
- Navigation is normal Blazor routing. There is no separate client-side router.

`wwwroot/` holds only static assets:

- CSS: `nav.css`, the `app.tailwind.css` source, and the compiled `tailwind.css`.
- `brand/` images.
- Minimal interop JS in `interop/` (`inspectorStore.js`, `captureStore.js` - localStorage bridges; no Tailwind classes).

The tabs:

- **Inspector** (`/inspector`) - builds, visualizes, and sends Egg, Inc. requests. See the Inspector send targets section.
- **Capture** (`/capture`) - the live capture dashboard. Start/stop the proxy, watch flows stream in.
- **Import** (`/import`) - import a HAR into `Endpoints/`. Local-only.
- **Protos** (`/protos`) - the proto registry console. Drop an `.ipa`/`.apk` to extract its `.proto` (everyone), browse detected builds per platform, diff two builds, and (contributor+) review the staged-proto queue. `/protos/{platform}/{build}` is one build's detail; `/protos/sources` credits the sources; `/protos/subscribe` manages proto feed webhooks.
- **Docs** (`/docs`) - browses `IDocRegistry` subjects (proto messages + endpoints) with editable DB-backed Markdown + tags. `<DocHelp>` gives inline help next to controls elsewhere.
- **Admin** (`/admin`) - user role management plus DB-contribution review. Admin-only.
- **Support** (`/support`) - supporter / donation info.

`TopNav` reads `GET /api/app/mode` and hides the Capture/Import/Admin links when the matching capability or role is off.

Middleware order matters in `Program.cs`: `UseStaticFiles()` must come before `UseRouting()`, otherwise `SimulationController`'s catch-all `[HttpOptions("/{**slug}")]` makes every static GET return 405.

## Adding an endpoint

Controllers are source-generated from `RouteMap/routes.yaml` by [EggIncognito.RouteGenerator](../EggIncognito.RouteGenerator/README.md). They land in `obj/` and are not checked in. Never edit generated files.

To add an endpoint, edit `routes.yaml` only:

```yaml
- path: ei/my_endpoint
  request: MyRequestType
  response: MyResponseType
```

Then run `dotnet build`. Add an endpoint file under `EggIncognito/Endpoints/default/<namespace>/` if a non-default response is needed.

For a path-parameterized route (the EID rides in the URL), add `pathParam: true`. Add `pathParamOnly: true` when there is no request body at all.

`routes.yaml` also carries `excluded:` (paths intentionally not mocked) and `endpoint_status:` (auto-generated ok/empty/missing buckets).

## Run modes

`IAppMode` (`Services/AppMode.cs`) splits the app into two modes via the `AppMode` config key.

- **Local** (default) - full features. Capture and endpoint writes mutate the local `Endpoints/`. The developer / self-host mode.
- **Hosted** (`AppMode=Hosted`) - the public deploy (`https://eggincognito.davidarthurcole.me`). A request must not mutate shared data, and the proxy/CA cannot be shared. So capture (`/api/capture/start`, `save-endpoint`) and writes (`/api/import/*`) return 403. The mock API, Inspector, and read-only Tools stay available.

`CaptureEnabled` / `WritesEnabled` override per capability. The UI reads `GET /api/app/mode`.

| | Local (default) | Hosted |
|---|---|---|
| Mock API + Inspector + Tools (read-only) | yes | yes |
| Capture proxy (`/capture`, `POST /api/capture/start`) | yes | 403 |
| HAR import + status rewrite (`/import`, `/api/import/*`) | yes | 403 |

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
| `Discord:BotToken` | unset | When set, starts the optional Discord bot. |
| `Discord:GuildId` | unset | Optional. Enables instant guild command registration for the bot. |
| `CaptureEnabled` | follows mode | Per-capability override for the capture proxy |
| `WritesEnabled` | follows mode | Per-capability override for endpoint writes (HAR import + status rewrite) |
| `ContentRoot` | auto-resolved | The dir holding `RouteMap/` + `Endpoints/` (resolved from the app/base dir; rarely set) |
| `EGG_INC_EID` | optional | EID used to scrub captured/imported data and as the in-app capture default |
| `CapturePort` | `8080` | Port the capture proxy listens on |
| `CapturePath` | `<content root>/captures` | Directory the capture HAR is written to |
| `CaPath` | `<CapturePath>/eggincognito-ca.cer` | The persisted capture root CA file |
| `EGG_INC_API_SALT` | required for request signing | API signing phrase for the Inspector's "Live API" sends |
| `Devices:*` | unset | Declares the wired phones to probe (`Id`/`Platform`/`Target`/`Label`/`Package` per device). Empty = no farm. |
| `DevicePolling:Enabled` / `:IntervalMinutes` | `true` / `30` | The background probe heartbeat. |
| `DeviceSync:Enabled` | `false` | Allow the heartbeat to drive a device store-update when its store is ahead. |
| `DeviceCapture:Enabled` / `:BasePort` / `:HostIp` | `false` / `9100` / auto | Persistent per-device capture proxies (rinfo harvest). `HostIp` = the address devices dial back to. |
| `DeviceCapture:Android:*` / `:Ios:*` | unset | CA-install scripts, iOS ssh creds + proxy/restart command templates (falls back to `DeviceUpdate:Ios`). |
| `DeviceUpdate:Ios:SshHost` / `:SshPort` / `:SshKeyPath` | unset / `2222` / unset | iOS ssh creds for binary pull + update + CA install. |
| `Feed:PageBaseUrl` | unset | Base URL stamped into proto feed notifications. |

## Web tooling

The former PowerShell scripts and CLI subcommands are gone. Their behavior is now web endpoints or build steps.

| Where | Purpose |
|---|---|
| Inspector Tools -> `GET /api/tools/postman-collection` | Download a Postman v2.1 collection from `routes.yaml` (was `export-collection`) |
| Inspector Tools -> `GET /api/tools/endpoint-status` | Report routes with a missing or empty endpoint (was `check-endpoints`) |
| `POST /api/import/endpoint-status/update` (Local) | Rewrite the `endpoint_status:` block (was `check-endpoints --update`) |
| Inspector Tools -> `POST /api/tools/decode` | Auto-detect the proto type of an arbitrary base64 blob (was `decode`) |
| Import tab -> `POST /api/import/har` (Local) | Import a HAR into `Endpoints/default/` (staged unless `?overwrite=true`) (was `from-har`) |
| `EmitDashboardTypes` MSBuild target | Regenerate `wwwroot/capture/types.d.ts` from the capture records on every build (was `emit-types`) |
| `BuildTailwind` MSBuild target | Compile `wwwroot/app.tailwind.css` -> `wwwroot/tailwind.css` with the standalone Tailwind CLI. See below. |

`BuildTailwind` details:

- No Node. The standalone Tailwind CLI is fetched to `tools/tailwind/` on first build (gitignored).
- Tokens live only in `tailwind.config.js`. All chrome is one canonical `@layer components` block in `app.tailwind.css`.
- `.btn-primary` is orange (`--accent`) everywhere. Blue (`--accent2`) is info/secondary.
- The Razor components emit semantic class names; the `@layer components` block defines them. Add a missing class to the component layer rather than inlining utility soup in markup.
- Skip with `-p:BuildTailwindCss=false`.
- Gotcha: the target runs `AfterTargets="Build"`, so a wwwroot-only incremental build does not recompile. Run `dotnet build EggIncognito/EggIncognito.csproj -t:BuildTailwind`.
- Gotcha: `@apply hidden` inside `@layer components` is a circular-dependency error. Use raw `display:none`.
- The fetch is RID-aware (right asset per OS + arch), so Linux/macOS CI compiles the sheet natively.

The live-API batch seed (former `seed` subcommand) is not yet web-ported. Recover it from git history if a Local seeding flow is needed.

## Inspector send targets

The Inspector's Send toggle has three targets.

- **Mock** - posts to this instance's own mock API (`location.origin`). Always available.
- **Live API** - the server's `/api/inspector/send` egresses to auxbrain. When `AppMode=Hosted` this is login-gated: an anonymous request gets 403. The server only egresses for authenticated users. Local is unrestricted. The Inspector greys out the toggle for anonymous hosted users by reading `GET /api/app/mode`.
- **Custom** - browser-direct. The browser POSTs the prebuilt form body straight to the user's own proxy URL (`inspector.customTarget` in localStorage). This server is fully bypassed, with zero egress. The bytes the browser receives are decoded by `POST /api/inspector/decode-response`, an egress-free pure-decode helper (proto reflection only, no network, no salt). The custom fetch is CORS-bound to whatever the user's proxy allows.

The signing salt stays client-owned (`inspector.salt`, sent only in `/build`). The server is never a signing oracle. `EGG_INC_API_SALT` is never read on the Inspector path.

`POST /api/inspector/decode-response` decodes both response framings: wrapped (`AuthenticatedMessage`, optionally compressed) and direct. It tries wrapped first, then falls back to direct.

## Proto registry API

A versioned store of Egg, Inc. `.proto` definitions, one row per app build. DB-backed: reads return empty / 404 without Postgres, writes return 503. Reads are public; writes are role-gated (contributor+ for additive, admin for destructive).

| Route | Method | Access | Purpose |
|---|---|---|---|
| `/api/tools/extract-proto` | POST | public (read limiter) | Carve the `.proto` out of an uploaded `.apk`/`.ipa`. Powers the `/protos` drop zone. |
| `/api/protos/versions` | GET | public | List detected builds, optionally `?platform=`. |
| `/api/protos/versions/{platform}/{build}` | GET | public | One build's metadata + message index. Add `/proto` for the raw `.proto` text. |
| `/api/protos/latest` | GET | public | Highest build for a platform. |
| `/api/protos/diff?from=&to=&platform=` | GET | public | Namespace-insensitive `.proto` diff between two builds. |
| `/api/protos/sources` | GET | public | Row count per source. |
| `/api/protos/versions` | POST | contributor+ | Promote an analyzed extraction into the registry. |
| `/api/protos/versions/{platform}/{build}` | PATCH | contributor+ | Correct a stored build's metadata. |
| `/api/protos/versions/{platform}/{build}` | DELETE | admin | Soft-delete a build (`DeletedAt`, hidden not removed). |
| `/api/protos/versions/delete` | POST | admin | Bulk soft-delete. |
| `/api/protos/versions/{platform}/{build}/restore` | POST | admin | Restore a soft-deleted / merged build. |
| `/api/protos/versions/merge-suggestions` | GET | public | Cross-platform releases sharing app version + proto SHA. |
| `/api/protos/versions/merge` | POST | admin | Merge aliases into a canonical build (reversible). |
| `/api/protos/staged/offer` | POST | public (write limiter) | Submit a proto for review (dedup-guarded). |
| `/api/protos/staged/check` | GET | public | Does this proto already exist (registry or pending)? |
| `/api/protos/staged/count` | GET | public | Pending count for the review badge. |
| `/api/protos/staged` | GET | contributor+ | The pending review queue. |
| `/api/protos/staged/{id}/approve` \| `/reject` | POST | contributor+ | Approve (promotes, with optional metadata edits) or reject. |
| `/api/protos/staged/bulk-approve` \| `/bulk-reject` | POST | contributor+ | Same, in bulk. |
| `/api/protos/staged/import-crawl` | POST | admin | Bulk-import the GitHub-crawl backfill dataset zip into staging. |
| `/api/protos/feed` (+ `/mine`, `/{id}`) | POST/GET/PATCH/DELETE | owner | Manage proto feed Discord-webhook subscriptions. |
| `/api/protos/backfill/*` | POST/GET | admin | Drive the version backfill crawlers + per-APK extract jobs (`BackfillController`). |

## Device farm API

A rack of wired Android + iOS phones, probed and updated and captured automatically. DB-backed and off by default. `DevicesController` (`/api/devices/*`): reads are public (empty `[]` without a DB), the mutating actions are admin-gated and 503 without a DB.

| Route | Method | Access | Purpose |
|---|---|---|---|
| `/api/devices/status` | GET | public | Latest probe per device, reclassified live against the registry. |
| `/api/devices/{id}/history` | GET | public | Recent probe rows for one device. |
| `/api/devices/{id}/live` | GET | public | Latest rinfo harvested off the wire, plus capture-boundary diagnostics (listening, client/auxbrain connects, flows, rinfo harvests, last decrypt error) so an empty rinfo is debuggable. |
| `/api/devices/{id}/refresh` | POST | admin | Run an immediate probe through the same runner the poller uses. |
| `/api/devices/{id}/check-update` | POST | admin | Tell the device to ask its own store for an update and install it. Fire-and-forget (202); poll `check-status`. |
| `/api/devices/{id}/check-status` | GET | admin | Live state of an in-flight check-update. |
| `/api/devices/{id}/save` | POST | admin | Pull the installed app, carve its proto in-process, harvest clientVersion off the wire, and upsert a registry row. |
| `/api/devices/{id}/restart-app` | POST | admin | Force-restart the app (kill + relaunch) so it hits auxbrain and a fresh rinfo can be captured. |

## Authentication (Discord OAuth, optional)

Cookie auth + `AspNet.Security.OAuth.Discord`, wired only when a DB AND `Discord:ClientId` + `Discord:ClientSecret` are configured (`AuthSetup.AddDiscordAuthIfConfigured`). Without that config the app is fully anonymous and unchanged.

- **Flow.** `GET /login` issues a Discord challenge. The middleware handles the `/discord-auth` callback, issues the cookie, and runs `UserUpsert.OnLoginAsync` (upserts the `users` row). `POST /logout` clears the cookie. `GET /api/auth/me` + `GET /api/app/mode` report the current user. `TopNav` renders login state from `/api/app/mode` (`authEnabled` + `user`).
- **Identity.** `User` entity in [EggIncognito.Data](../EggIncognito.Data/README.md) (`discord_id` PK). `ICurrentUser` (web) exposes the Discord claims server-side. `AuthState` is a singleton flag for the wired/not-wired branch.
- **Operator setup.** Register the OAuth app's redirect URI in the Discord developer portal: `https://eggincognito.davidarthurcole.me/discord-auth` (prod) and `http://localhost:5032/discord-auth` (local dev). Scope: `identify` (no email).

## Authorization (roles + ACLs)

`User.Role` (viewer / contributor / admin) is resolved on login and stamped into the cookie as the `egi:role` claim. Two orthogonal authorities:

- `IAppMode.CanWrite` gates local file ops (`/api/import/*`, capture). These have no user and run only in Local mode.
- The role gate authorizes shared-DB writes: `/api/db/endpoint` + `/api/db/route` require contributor+ (403 otherwise). Reads stay public. This replaces the old `CanWrite` check on those actions.

The first admin is bootstrapped from `Discord:AdminIds`. New users default to viewer. `/api/admin/*` (admin-only) lists/promotes users and reviews/deletes DB contributions. `/admin` is the admin page, with its nav link shown only to admins. Viewers/anonymous who try to save get a browser-`localStorage` fallback. The role check + admin self-lockout guard run before any DB access, so they are testable DB-free.

## Rate limiting

Built-in `Microsoft.AspNetCore.RateLimiting` (`Services/RateLimiting/`). `UseRateLimiter()` is placed after auth (the partition key needs the authenticated user) and before `MapControllers`.

There is no global backstop and no read policy. Page loads, static assets, the SSE stream, and read APIs are never throttled. Only two named policies are wired:

- `egress` - the Inspector Live-API `send`.
- `write` - the import/db/docs/admin controllers.

Tiers (requests/min):

| Tier | Limit |
|---|---|
| anon | 30 |
| viewer | 120 |
| contributor+ | 600 |

- **Effective permit** = `min(policy, tier)`, so anonymous callers stay strictest.
- **Admins are exempt entirely.** `RateLimiterSetup.IsExempt` returns true for `IsAtLeast(Admin)`, so an admin's partition is `GetNoLimiter`. The device farm + admin tooling legitimately burst (poll loops, force-restart + capture), which a per-tier cap would throttle.
- **Partition.** `user:{DiscordId}` when logged in, else `ip:{clientIp}` from `CF-Connecting-IP`. The `X-Forwarded-For` first hop and the socket IP are local/no-CF fallbacks.
- **Rejections.** 429 + `Retry-After` + a small JSON body.
- **Config.** Driven by the `RateLimiting` appsettings section. `Enabled:false` makes it a no-op. `RateLimitOptions` / `RateLimitKeys` are pure + unit-tested.
- **Security note.** Trusting `CF-Connecting-IP` is safe only while Cloudflare is the only path to the origin.

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
