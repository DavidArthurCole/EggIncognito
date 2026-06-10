<h1 align="center">EggIncognito</h1>

<p align="center">
  <a href="https://github.com/DavidArthurCole/EggIncognito/actions/workflows/ci.yml"><img src="https://img.shields.io/github/actions/workflow/status/DavidArthurCole/EggIncognito/ci.yml?branch=main" alt="CI"></a>
  <a href="https://discord.davidarthurcole.me"><img src="https://img.shields.io/badge/discord-join%20server-5865F2?logo=discord&logoColor=white" alt="Discord"></a>
</p>

A stateless mock of the [Egg, Inc.](https://auxbrain.com/) API. It serves real captured responses from disk. The format is the same base64-protobuf used by auxbrain.com, so your tools can't tell the difference.

Use it to test Egg, Inc. tooling without real accounts, rate limits, or hitting the live API.

<h1 align="center">Quick Start</h1>

<h3 align="center">Download a Pre-built Server</h3>

Grab a self-contained binary for your platform from the [latest release](https://github.com/DavidArthurCole/EggIncognito/releases/latest). No .NET install is needed. Builds are available for `linux-x64`, `linux-arm64`, `osx-arm64`, and `win-x64`. Unpack and run the `EggIncognito` executable. The `Endpoints/` response set ships alongside it.

Or run the container image:

```sh
docker run -p 5080:8080 ghcr.io/davidarthurcole/eggincognito:latest
```

Or clone this repo and run from source:

```sh
dotnet run --project EggIncognito
```

Or with Docker Compose:

```sh
docker compose up
```

All listen on `http://localhost:5080`. Point your Egg, Inc. client at that address.

Run the tests with:

```sh
dotnet test EggIncognito.slnx
```

## Configuring responses

Responses come from endpoint files on disk. They live in `EggIncognito/Endpoints/`, grouped by API namespace:

```
Endpoints/
  default/
    ei/first_contact_secure.json
    ei_afx/config.json
    ...
  eids/
    EI0000000000000001/
      ei/first_contact_secure.json
      ...
```

The endpoint key is the API path, for example `ei/first_contact_secure`. The server checks for a per-EID endpoint first, then falls back to the default.

Endpoint JSON is [Google.Protobuf JSON](https://protobuf.dev/programming-guides/proto3/#json) in camelCase. An empty `{}` returns all-default field values. A missing endpoint behaves the same way. To change a response, edit or add the matching `.json` file.

**Per-EID overrides:** drop a file at `Endpoints/eids/<EID>/<path>.json`. The server reads `user_id` from the incoming request to pick the right directory.

## Capture your own data

The app includes a TLS-intercepting capture proxy. It records real Egg, Inc. traffic from a phone and writes it out as endpoints. It decrypts only auxbrain hosts and tunnels everything else untouched, so the rest of the phone keeps working.

Open the Capture tab at `http://localhost:5080/capture/` and hit Start capture. Or auto-start the proxy at launch:

```sh
dotnet run --project EggIncognito -- --capture
```

Point the phone's Wi-Fi proxy at your computer, install and trust the printed certificate, and play. Each captured flow becomes an endpoint on disk. The [capture engine README](EggIncognito.Capture/README.md) has the full device walkthrough, including the iOS certificate-trust step that trips people up.

## Going deeper

Each project has its own README:

- [EggIncognito](EggIncognito/README.md) - the web app: wire format, the Blazor Server frontend, adding endpoints, config, run modes, web tooling, auth, rate limiting.
- [EggIncognito.Core](EggIncognito.Core/README.md) - shared services + the `Ei.*` proto types.
- [EggIncognito.Capture](EggIncognito.Capture/README.md) - the capture engine + the full device-capture walkthrough.
- [EggIncognito.Data](EggIncognito.Data/README.md) - the optional Postgres layer.
- [EggIncognito.Bot](EggIncognito.Bot/README.md) - the optional Discord bot.
- [EggIncognito.RouteGenerator](EggIncognito.RouteGenerator/README.md) - the Roslyn source generator.
- [EggIncognito.Tests](EggIncognito.Tests/README.md) - the test suite.

EggIncognito is built on .NET / ASP.NET Core. Its mock controllers are source-generated from a single `routes.yaml`, and its UI is a Blazor Server app (Razor Components in `EggIncognito/Components/`, InteractiveServer render mode over a SignalR circuit). See the [web app README](EggIncognito/README.md) for how it fits together.

### HTTPS (optional)

Place `server.crt` and `server.key` (PEM) in a `certs/` directory. Kestrel detects them at startup and enables HTTPS automatically. Locally this serves `https://localhost:5443`. For Docker, mount `./certs:/app/certs:ro`. The `certs/` directory is gitignored, so never commit private keys. See the [web app README](EggIncognito/README.md) for the related config keys.
