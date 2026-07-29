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

The UI is Blazor Server (Razor Components). A host page loads the routable pages over a shared main layout plus the `TopNav`. Reusable pieces (`Icon`, `Markdown`, `MarkdownEditor`, `DocHelp`) live in a shared components area. Interactivity is `InteractiveServer` over a SignalR circuit.

- Pages: `/` (Home), `/inspector`, `/capture`, `/import`, `/protodata` (Protos & Data, also served at `/protos` + `/data` + `/periodicals`), `/playground` (admin), `/docs`, `/admin`, `/support`.
- Navigation is normal Blazor routing. There is no separate client-side router.

Static assets are the CSS layer (a small nav sheet plus the compiled `styles.css` generated from `Styles/app.v4.css`), brand images, and minimal localStorage-bridge interop JS for the Inspector and Capture. The interop JS carries no utility classes.

The tabs:

- **Inspector** (`/inspector`) - builds, visualizes, and sends Egg, Inc. requests. See the Inspector send targets section.
- **Capture** (`/capture`) - the live capture dashboard. Start/stop the proxy, watch flows stream in.
- **Import** (`/import`) - import a HAR into the endpoint store. Local-only.
- **Protos & Data** (`/protodata`, also `/protos` + `/data` + `/periodicals`) - one page with `.tab-bar` sub-tabs. Registry (public): drop an `.ipa`/`.apk` to extract its `.proto` (everyone), browse detected builds per platform, diff two builds, and (contributor+) review the staged-proto queue. Periodicals + Data API (admin-only): the game-data repository (families, eiafx_data, raw feeds, colleggtibles) and the public data API source index plus API-key management. `/protos/{platform}/{build}` is one build's detail; `/protos/sources` credits the sources; `/protos/subscribe` manages proto feed webhooks.
- **Docs** (`/docs`) - browses `IDocRegistry` subjects (proto messages + endpoints) with editable DB-backed Markdown + tags. `<DocHelp>` gives inline help next to controls elsewhere.
- **Admin** (`/admin`) - user role management, DB-contribution review, API activity, and API-key oversight. Admin-only.
- **Support** (`/support`) - supporter / donation info.

`TopNav` reads `GET /api/app/mode` and hides the Capture/Import/Admin links when the matching capability or role is off.

Middleware order matters: `UseStaticFiles()` must come before `UseRouting()`, otherwise `SimulationController`'s catch-all `[HttpOptions("/{**slug}")]` makes every static GET return 405.

## Adding an endpoint

Controllers are source-generated from the route map by [EggIncognito.RouteGenerator](../EggIncognito.RouteGenerator/README.md). The generated files are not checked in. Never edit them.

To add an endpoint, edit the route map only:

```yaml
- path: ei/my_endpoint
  request: MyRequestType
  response: MyResponseType
```

Then run `dotnet build`. Add a namespaced endpoint file to the default store if a non-default response is needed.

For a path-parameterized route (the EID rides in the URL), add `pathParam: true`. Add `pathParamOnly: true` when there is no request body at all.

The route map also carries `excluded:` (paths intentionally not mocked) and `endpoint_status:` (auto-generated ok/empty/missing buckets).

## Run modes

`IAppMode` splits the app into two modes via the `AppMode` config key.

- **Local** (default) - full features. Capture and endpoint writes mutate the local endpoint store. The developer / self-host mode.
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
| `CertsPath` | `<app dir>/certs` | Directory containing the server certificate and key |
| `HttpPort` | `8080` | HTTP port when certs are present (overrides ASPNETCORE_URLS) |
| `HttpsPort` | `8443` | HTTPS port (only active when certs are present) |
| `AppMode` | `Local` | `Local` = full features; `Hosted` = capture + writes disabled (the public deploy) |
| `ConnectionStrings:Postgres` | unset | When set, enables the Postgres data layer (overlay + DB routes) and applies migrations at startup. Unset = file-only. |
| `Identity:ApiUrl` / `Identity:ApiSecret` | unset | External EggIdentity service URL + shared secret. Cookie auth wires only when both are set. Users/roles live in that service, not locally. |
| `Discord:BotToken` | unset | When set, starts the optional Discord bot. |
| `Discord:GuildId` | unset | Optional. Enables instant guild command registration for the bot. |
| `CaptureEnabled` | follows mode | Per-capability override for the capture proxy |
| `WritesEnabled` | follows mode | Per-capability override for endpoint writes (HAR import + status rewrite) |
| `ContentRoot` | auto-resolved | The dir holding the route map + endpoint store (resolved from the app/base dir; rarely set) |
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
| Inspector Tools -> `GET /api/tools/postman-collection` | Download a Postman v2.1 collection from the route map (was `export-collection`) |
| Inspector Tools -> `GET /api/tools/endpoint-status` | Report routes with a missing or empty endpoint (was `check-endpoints`) |
| `POST /api/import/endpoint-status/update` (Local) | Rewrite the `endpoint_status:` block (was `check-endpoints --update`) |
| Inspector Tools -> `POST /api/tools/decode` | Auto-detect the proto type of an arbitrary base64 blob (was `decode`) |
| Import tab -> `POST /api/import/har` (Local) | Import a HAR into the default endpoint store (staged unless `?overwrite=true`) (was `from-har`) |
| `EmitDashboardTypes` MSBuild target | Regenerate the capture dashboard type declarations from the capture records on every build (was `emit-types`) |
| `BuildStyles` MSBuild target | Default-on: compile the served stylesheet from the CSS source via the `EggIncognito.CssBuild` tool. Skip with `-p:BuildStylesCss=false`. See below. |

Stylesheet details:

- The served sheet `wwwroot/styles.css` is generated on every build and gitignored. Compilation is pure C#: the `EggIncognito.CssBuild` console tool runs MonorailCss (a Tailwind-v4-compatible engine) over `Styles/app.v4.css`, merged with the shared `EggIdentity.Styles` component classes. No CLI download, no Node, no executable anywhere.
- To restyle: edit `Styles/app.v4.css` and rebuild. Tokens live in its `@theme` block; app chrome is its component classes; the app's own definitions win any name collision with the shared `EggIdentity.Styles` set.
- The Razor components emit semantic class names; the source sheet defines them. Add a missing class there rather than inlining utilities in markup.
- Utility class names used only in dynamically-built C# strings are still found because the tool scans `.cs` files; genuinely invisible names go in the tool's `ContentSafelist`.
- The 1:1 visual gate is the `EggIdentity.StyleVerify` computed-style harness against the golden baselines in the EggIdentity repo, never a text diff of the sheets.

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
| `/api/protos/staged/import-crawl` | POST | admin | Bulk-import a proto-crawl dataset zip into staging (manual admin upload). |
| `/api/protos/feed` (+ `/mine`, `/{id}`) | POST/GET/PATCH/DELETE | owner | Manage proto feed Discord-webhook subscriptions. |

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

## Authentication (EggIdentity, optional)

Cookie auth backed by the external EggIdentity microservice (`IdentityApiClient`), wired only when `Identity:ApiUrl` + `Identity:ApiSecret` are set (`AuthSetup.AddEggIdentityAuthIfConfigured`, cookie `egi.auth`, 30-day sliding). There is no local user table (dropped 2026-07-08); users, roles, and the admin allowlist all live in the identity service. Without that config the app is fully anonymous and unchanged.

- **Flow.** Login happens through the identity widget (Discord OAuth is inside that service, not here). The app redeems a login code, signs in the cookie, and caches the claims `egi:user_id` / `egi:role` / `egi:supporter`. `POST /logout` clears the cookie. On each request `OnValidatePrincipal` asks the identity service whether the session was revoked (skipped gracefully if the service is unreachable). `GET /api/auth/me` + `GET /api/app/mode` report the current user; `TopNav` renders login state from `/api/app/mode`.
- **Identity.** `ICurrentUser` (web) reads the cached claims server-side. `AuthState` is a singleton flag for the wired/not-wired branch. Row ownership stores the identity `Guid` with no local foreign key.
- **API keys.** `AuthSetup` also chains an `ApiKey` authentication scheme so a request carrying `X-Api-Key` / `Bearer egi_` authenticates as a viewer (reads + higher rate tier only, never write/admin). See [API access + keys](#api-access--keys).

## Authorization (roles + ACLs)

The role (viewer / contributor / admin) rides in the cookie's `egi:role` claim, resolved by the identity service. Two orthogonal authorities:

- `IAppMode.CanWrite` gates local file ops (`/api/import/*`, capture). These have no user and run only in Local mode.
- The role gate authorizes shared-DB writes: `/api/db/endpoint` + `/api/db/route` require contributor+ (403 otherwise). Reads stay public.

New users default to viewer. `AdminController.SetUserRole` proxies the identity service's `SetRoleAsync`. `/api/admin/*` (admin-only) lists/promotes users and reviews/deletes DB contributions. `/admin` is the admin page, with its nav link shown only to admins. Viewers/anonymous who try to save get a browser-`localStorage` fallback. The role check + admin self-lockout guard run before any DB access.

## API access + keys

Every `/api/*` MVC action declares an explicit floor via `[ApiAccess(ApiAccessLevel.{Public|Authenticated|Contributor|Admin})]` (class or action; action wins). The global `ApiAccessFilter` enforces default-DENY: a `/api/*` action with no attribute returns 500, so it cannot ship. The attribute is a floor only; the in-body guards above (`CanWrite`, hosted-block, `RequireAdmin`/`RequireContributor`, owner/supporter checks) still run and can be stricter.

- **Keys.** Any logged-in user mints/lists/revokes their own keys at `/api/v1/keys/*` (`ApiKeysController`); the self-service UI lives in the Data API sub-tab of `/protodata`. Admins view + revoke all keys at `/api/admin/api-keys`. A key is `egi_live_<rand>`, stored as a SHA-256 hash + 12-char prefix, shown once. Cap `ApiKeys:MaxPerUser` (default 20). DB-gated: writes 503 without Postgres.
- **Public data API.** `GET /api/v1/data` (index) + `/api/v1/data/{group}/{id}` (dispatch) serve every game-data point from the self-describing `DataCatalog`, so each source gets a URL automatically and nothing is hardcoded elsewhere. Groups: `periodical` (wire fixtures, authenticated, egress-refreshable), `derived` + `gamedata` (public, extracted/embedded), `asset` (icon PNG, public, `?name=`). Anonymous callers get 1 request/hour; log in or send `X-Api-Key`.

## Rate limiting

Built-in `Microsoft.AspNetCore.RateLimiting`. `UseRateLimiter()` is placed after auth (the partition key needs the authenticated user) and before `MapControllers`. Default-on; `RateLimiting:Enabled=false` makes it a pass-through. A policy applies only where a controller opts in with `[EnableRateLimiting("...")]`; unmarked page loads, static assets, and the SSE stream are never throttled.

Named policies (requests/min unless noted):

| Policy | Limit | Applied to |
|---|---|---|
| `egress` | 10 | the Inspector Live-API `send` |
| `write` | 60 | import / db / docs / admin writes |
| `read` | 120 | read APIs |
| `fetch` | 300 | high-frequency status polls (device status/live) |
| `data` | see below | the public data API (`/api/v1/data`) |
| `Global` | 300 | backstop |

Tiers (requests/min):

| Tier | Limit |
|---|---|
| anon | 30 |
| viewer | 120 |
| contributor | 600 |
| supporter | 1200 |
| keyed (API-key callers) | 600 |

- **Effective permit** = `min(policy, tier)`, so anonymous callers stay strictest.
- **`data` policy** is custom (`DataPartition`): anonymous = 1 request/HOUR by ip (the anti-scrape gate), API-key = the keyed tier by `apikey:{id}`, cookie = the role tier by `user:{id}`.
- **Admins are exempt entirely.** `RateLimiterSetup.IsExempt` returns true for `IsAtLeast(Admin)`, so an admin's partition is `GetNoLimiter`. The device farm + admin tooling legitimately burst (poll loops, force-restart + capture), which a per-tier cap would throttle.
- **Partition.** `user:{id}` when logged in, else `ip:{clientIp}` from `CF-Connecting-IP`. The `X-Forwarded-For` first hop and the socket IP are local/no-CF fallbacks.
- **Rejections.** 429 + `Retry-After` + a small JSON body.
- **Config.** Driven by the `RateLimiting` appsettings section. `RateLimitOptions` / `RateLimitKeys` are pure.
- **Security note.** Trusting `CF-Connecting-IP` is safe only while Cloudflare is the only path to the origin.

## Distribution

The app ships two ways. Both are produced by the `Release` workflow on a `v*` tag.

- **Self-contained binaries** - `dotnet publish --self-contained -p:PublishSingleFile=true` per runtime: `linux-x64`, `linux-arm64`, `osx-arm64`, `win-x64`. No .NET install needed. The endpoint response set is bundled alongside the executable. Attached to the GitHub release.
- **Container image** - `ghcr.io/davidarthurcole/eggincognito:<tag>` and `:latest`, built from the repo's container image definition.

To build a single-file binary locally:

```sh
dotnet publish EggIncognito -c Release -r linux-x64 \
  --self-contained true -p:PublishSingleFile=true -o ./publish
cp -r EggIncognito/Endpoints ./publish/Endpoints
```
