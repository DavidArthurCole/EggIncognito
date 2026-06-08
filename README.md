<h1 align="center">EggIncognito</h1>

<p align="center">
  <a href="https://github.com/DavidArthurCole/EggIncognito/actions/workflows/ci.yml"><img src="https://img.shields.io/github/actions/workflow/status/DavidArthurCole/EggIncognito/ci.yml?branch=main" alt="CI"></a>
  <a href="https://discord.davidarthurcole.me"><img src="https://img.shields.io/badge/discord-join%20server-5865F2?logo=discord&logoColor=white" alt="Discord"></a>
</p>

A stateless mock of the [Egg, Inc.](https://auxbrain.com/) API. It serves real captured responses from disk in the same base64-protobuf format as auxbrain.com - so your tools can't tell the difference.

Use it to test Egg, Inc. tooling without real accounts, rate limits, or hitting the live API.

<h1 align="center">Quick Start</h1>

<h3 align="center">Download a Pre-built Server</h3>

Grab a self-contained binary for your platform from the [latest release](https://github.com/DavidArthurCole/EggIncognito/releases/latest) (no .NET install needed) - `linux-x64`, `linux-arm64`, `osx-arm64`, or `win-x64`. Unpack and run the `EggIncognito` executable; the `Endpoints/` response set ships alongside it.

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

The endpoint key is the API path (e.g. `ei/first_contact_secure`). The server checks for a per-EID endpoint first, then falls back to the default.

Endpoint JSON is [Google.Protobuf JSON](https://protobuf.dev/programming-guides/proto3/#json) (camelCase). An empty `{}` returns all-default field values, and a missing endpoint behaves the same way. To change a response, just edit (or add) the matching `.json` file.

**Per-EID overrides:** drop a file at `Endpoints/eids/<EID>/<path>.json`. The server reads `user_id` from the incoming request to pick the right directory.

## Capture your own data

The app includes a TLS-intercepting capture proxy that records real Egg, Inc. traffic from a phone and writes it out as endpoints. It decrypts only auxbrain hosts and tunnels everything else untouched, so the rest of the phone keeps working.

Open the Capture tab at `http://localhost:5080/capture/` and hit Start capture, or auto-start the proxy at launch:

```sh
dotnet run --project EggIncognito -- --capture
```

Point the phone's Wi-Fi proxy at your computer, install and trust the printed certificate, and play. Each captured flow becomes an endpoint on disk. [CAPTURE.md](CAPTURE.md) has the full device walkthrough, including the iOS certificate-trust step that trips people up.

## Going deeper

- [CAPTURE.md](CAPTURE.md) - capturing real device traffic, re-running captures, troubleshooting.
- [TECHNICAL.md](TECHNICAL.md) - wire format, architecture, adding endpoints, configuration, CLI subcommands.

EggIncognito is built on .NET / ASP.NET Core; its controllers are source-generated from a single `routes.yaml`. See [TECHNICAL.md](TECHNICAL.md) for how it fits together.

### HTTPS (optional)

Place `server.crt` and `server.key` (PEM) in a `certs/` directory. Kestrel detects them at startup and enables HTTPS automatically (`https://localhost:5443` locally; mount `./certs:/app/certs:ro` for Docker). The `certs/` directory is gitignored - never commit private keys. See [TECHNICAL.md](TECHNICAL.md#configuration) for the related config keys.
