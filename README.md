<h1 align="center">EggIncognito</h1>

<p align="center">
  <a href="https://github.com/DavidArthurCole/EggIncognito/actions/workflows/ci.yml"><img src="https://img.shields.io/github/actions/workflow/status/DavidArthurCole/EggIncognito/ci.yml?branch=main" alt="CI"></a>
  <a href="https://discord.davidarthurcole.me"><img src="https://img.shields.io/badge/discord-join%20server-5865F2?logo=discord&logoColor=white" alt="Discord"></a>
</p>

A toolkit for the [Egg, Inc.](https://auxbrain.com/) API. It started as a stateless mock server that replays real captured responses in the exact base64-protobuf format auxbrain.com uses, so your tools can't tell the difference. It has since grown a request inspector, a live capture proxy, a versioned proto registry, and a physical device farm.

Use it to test Egg, Inc. tooling without real accounts or rate limits, to build and decode API requests by hand, to capture live traffic into reusable fixtures, and to keep an authoritative record of the game's proto schema across versions.

The public instance runs at [eggincognito.davidarthurcole.me](https://eggincognito.davidarthurcole.me).

## What it does

| Feature | Page | What it is |
|---|---|---|
| Mock API | (POST endpoints) | Serves canned protobuf responses per route + per EID, byte-identical to auxbrain. |
| Inspector | `/inspector` | Build, sign, send, and decode any API request. Every transform is visualized. |
| Capture | `/capture` | TLS-intercepting proxy. Records live game traffic into reusable endpoints. |
| Protos & Data | `/protodata` | Versioned proto registry (drop an `.ipa`/`.apk` to extract its schema) plus the game-data repository and the public data API + key management. |
| Docs | `/docs` | Per-message + per-endpoint documentation with tags and a proto reference. |
| Import | `/import` | Turn a captured HAR into endpoints. |
| Admin | `/admin` | User roles + shared-store review + API-key oversight. |

## Quick start

Grab a self-contained binary for your platform from the [latest release](https://github.com/DavidArthurCole/EggIncognito/releases/latest). No .NET install needed. Builds: `linux-x64`, `linux-arm64`, `osx-arm64`, `win-x64`. Unpack, run the `EggIncognito` executable. The response set ships alongside it.

Or the container image:

```sh
docker run -p 5080:8080 ghcr.io/davidarthurcole/eggincognito:latest
```

Or from source:

```sh
dotnet run --project EggIncognito
```

Or with Docker Compose:

```sh
docker compose up
```

All listen on `http://localhost:5080`. Point your Egg, Inc. client at that address.

## Configuring responses

Responses come from endpoint files on disk, grouped by API namespace:

```
Endpoints/
  default/
    ei/first_contact_secure.json
    ei_afx/config.json
  eids/
    EI0000000000000001/
      ei/first_contact_secure.json
```

The endpoint key is the API path (`ei/first_contact_secure`). The server checks for a per-EID endpoint first, then falls back to the default.

Endpoint JSON is [Google.Protobuf JSON](https://protobuf.dev/programming-guides/proto3/#json) in camelCase. An empty `{}` returns all-default field values. A missing endpoint behaves the same way. Edit or add the matching `.json` file to change a response. The server reads `user_id` from the request to pick the per-EID directory.

When a Postgres connection string is set, stored DB rows can overlay these file defaults without changing the serving contract. See the [data layer README](EggIncognito.Data/README.md).

## Capture your own data

The app embeds a TLS-intercepting proxy. It records real Egg, Inc. traffic from a phone and writes it out as endpoints. It decrypts only auxbrain hosts and tunnels everything else untouched, so the rest of the phone keeps working.

Open `/capture/` and hit Start capture, or auto-start the proxy at launch:

```sh
dotnet run --project EggIncognito -- --capture
```

Point the phone's Wi-Fi proxy at your computer, install and trust the printed certificate, and play. Each captured flow becomes an endpoint on disk. The [capture engine README](EggIncognito.Capture/README.md) has the full device walkthrough, including the iOS certificate-trust step, plus the automated device-farm path.

## Proto registry

`/protos` keeps a versioned record of the Egg, Inc. proto schema. Drop an `.ipa` or `.apk` and it carves the `.proto` schema out of the binary in-process (no external toolchain), shows the diff against what is on record, and (for contributors) stages it for review. Browse detected builds per platform, see clientVersion / build / appVersion, and read the schema. The registry is DB-backed and empty without Postgres. See the [data layer README](EggIncognito.Data/README.md) and [Core README](EggIncognito.Core/README.md).

## Going deeper

| Project | What it covers |
|---|---|
| [EggIncognito](EggIncognito/README.md) | The web app: wire format, Blazor Server frontend, adding endpoints, config, run modes, web tooling, auth, rate limiting, the proto + device APIs. |
| [EggIncognito.Core](EggIncognito.Core/README.md) | Shared services, proto extraction, the `Ei.*` proto types. |
| [EggIncognito.Capture](EggIncognito.Capture/README.md) | The capture engine + the manual and device-farm capture walkthroughs. |
| [EggIncognito.Data](EggIncognito.Data/README.md) | The optional Postgres layer (endpoint overlay, proto registry, device farm, docs). |
| [EggIncognito.Bot](EggIncognito.Bot/README.md) | The optional Discord bot. |
| [EggIncognito.RouteGenerator](EggIncognito.RouteGenerator/README.md) | The Roslyn source generator that turns the route map into controllers. |

For the internals overview see [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md). EggIncognito is .NET / ASP.NET Core. Its mock controllers are source-generated from a single route map, and its UI is a Blazor Server app (Razor Components, InteractiveServer render mode over a SignalR circuit).

## Run modes

`IAppMode` splits the app into Local (default, full features) and Hosted (the public deploy: no shared-data mutation, capture and writes return 403). See the [web app README](EggIncognito/README.md#run-modes).

## HTTPS (optional)

Place a PEM certificate and key in a `certs/` directory. Kestrel detects them at startup and enables HTTPS automatically. Locally this serves `https://localhost:5443`. For Docker, mount `./certs:/app/certs:ro`. The `certs/` directory is gitignored, so never commit private keys.
