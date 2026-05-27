<h1 align="center">EggIncognito</h1>

<p align="center">
  <a href="https://github.com/DavidArthurCole/EggIncognito/actions/workflows/ci.yml"><img src="https://img.shields.io/github/actions/workflow/status/DavidArthurCole/EggIncognito/ci.yml?branch=main" alt="CI"></a>
  <a href="https://discord.davidarthurcole.me"><img src="https://img.shields.io/badge/discord-join%20server-5865F2?logo=discord&logoColor=white" alt="Discord"></a>
</p>

<p align="center">
  <a href="https://github.com/DavidArthurCole/EggIncognito/tree/generated/go"><img src="https://img.shields.io/badge/Go-00ADD8?logo=go&logoColor=white" alt="Go"></a>
  <a href="https://github.com/DavidArthurCole/EggIncognito/tree/generated/python"><img src="https://img.shields.io/badge/Python-3776AB?logo=python&logoColor=white" alt="Python"></a>
  <a href="https://github.com/DavidArthurCole/EggIncognito/tree/generated/javascript"><img src="https://img.shields.io/badge/JavaScript-F7DF1E?logo=javascript&logoColor=black" alt="JavaScript"></a>
  <a href="https://github.com/DavidArthurCole/EggIncognito/tree/generated/java"><img src="https://img.shields.io/badge/Java-ED8B00?logo=openjdk&logoColor=white" alt="Java"></a>
  <a href="https://github.com/DavidArthurCole/EggIncognito/tree/generated/kotlin"><img src="https://img.shields.io/badge/Kotlin-7F52FF?logo=kotlin&logoColor=white" alt="Kotlin"></a>
  <a href="https://github.com/DavidArthurCole/EggIncognito/tree/generated/ruby"><img src="https://img.shields.io/badge/Ruby-CC342D?logo=ruby&logoColor=white" alt="Ruby"></a>
  <a href="https://github.com/DavidArthurCole/EggIncognito/tree/generated/csharp"><img src="https://img.shields.io/badge/C%23-512BD4?logo=dotnet&logoColor=white" alt="C#"></a>
</p>

Stateless mock server for the [Egg, Inc.](https://eggincgame.com/) API. Accepts the same base-64 protobuf POST format as the real API, and returns configurable fixture responses from disk.

## Quick start

Run locally:

```sh
dotnet run --project EggIncognito
```

Or with Docker (fixtures volume-mounted from `./EggIncognito/Fixtures`):

```sh
docker compose up
```

Both listen on `http://localhost:5080`. To run tests:

```sh
dotnet test EggIncognito.slnx
```

### HTTPS

Place `server.crt` and `server.key` (PEM format) in a `certs/` directory. Kestrel detects them at startup and enables HTTPS automatically.

```
certs/
  server.crt
  server.key
```

- Local: `https://localhost:5443`
- Docker: mount `./certs:/app/certs:ro` (already in `docker-compose.yml`)
- `CertsPath`, `HttpPort`, and `HttpsPort` config keys override the defaults
- The `certs/` directory is gitignored - never commit private keys

## Fixtures

Fixtures live in `EggIncognito/Fixtures/`. The store checks for a per-EID fixture first, then falls back to the default.

```
Fixtures/
  default/
    ei_first_contact_secure.json
    ...
  eids/
    EI0000000000000001/
      ei_first_contact_secure.json
      ...
```

Slug = path with `/` replaced by `_`. E.g. `ei/first_contact_secure` -> `ei_first_contact_secure.json`.

JSON format is [Google.Protobuf JSON](https://protobuf.dev/programming-guides/proto3/#json) (camelCase). An empty `{}` returns all-default field values.

### Per-EID overrides

Drop a file at `Fixtures/eids/<EID>/<slug>.json`. The server extracts `user_id` from the incoming `AuthenticatedMessage` to pick the right directory.

### Seeding from the real API

```sh
# PowerShell
$env:EGG_INC_EID = 'EI...'
dotnet run --project EggIncognito.Seeder

# bash/zsh
EGG_INC_EID='EI...' dotnet run --project EggIncognito.Seeder
```

Writes fixtures to `Fixtures/eids/<EID>/`. Real EID directories are gitignored by default - see `Fixtures/eids/.gitignore`.

## Configuration

| Variable | Default | Description |
|---|---|---|
| `FixturesPath` | `<app dir>/Fixtures` | Fixtures root |
| `ASPNETCORE_URLS` | `http://+:8080` | Bind address (used in Docker, overridden when certs present) |
| `CertsPath` | `<app dir>/certs` | Directory containing `server.crt` and `server.key` |
| `HttpPort` | `8080` | HTTP port when certs are present (overrides ASPNETCORE_URLS) |
| `HttpsPort` | `8443` | HTTPS port (only active when certs are present) |
| `EGG_INC_EID` | required | EID for the Seeder CLI |
| `EGG_INC_API_SALT` | required | API authentication phrase for the Seeder CLI |

## Scripts

All scripts are PowerShell Core and run cross-platform with `pwsh`.

| Script | Purpose |
|---|---|
| `Sync-Proto.ps1` | Download latest `ei.proto` from [elgranjero/EggIncProtos](https://github.com/elgranjero/EggIncProtos) |
| `Sync-Endpoints.ps1` | Diff APK endpoint strings against `endpoints.yaml` |
| `Export-Collection.ps1` | Generate a Postman v2.1 collection JSON from `endpoints.yaml` |
| `Extract-Fixtures.ps1` | Extract fixture files from a mitmproxy `.mitm` capture |
| `Publish-Fixtures.ps1` | Push fixture data to the orphan `fixtures` branch |
| `Publish-Generated.ps1` | Generate and push each language server to its `generated/<lang>` branch |

```sh
pwsh scripts/Sync-Proto.ps1
pwsh scripts/Sync-Endpoints.ps1 -ApkPath ./apks/split_config.arm64_v8a.apk
pwsh scripts/Sync-Endpoints.ps1 -GooglePlayEmail you@gmail.com
pwsh scripts/Export-Collection.ps1
pwsh scripts/Extract-Fixtures.ps1 -FlowsFile ./captures/session.mitm
pwsh scripts/Publish-Fixtures.ps1
pwsh scripts/Publish-Generated.ps1
```

The exported collection (`EggIncognito-postman-collection.json`) can be imported into Postman via **File > Import**. Set the `baseUrl` collection variable to your server address before sending requests.

## Wire format

- **Request**: `POST /<path>`, body `data=<base64(proto bytes)>` (`application/x-www-form-urlencoded`)
- **Response**: base64-encoded proto bytes, `Content-Type: text/html`

## Architecture

| Project | Purpose |
|---|---|
| `EggIncognito` | ASP.NET Core net10.0 - controllers, fixtures, startup |
| `EggIncognito.Generator` | Roslyn `IIncrementalGenerator` (netstandard2.0) - reads `endpoints.yaml`, emits one controller per endpoint |
| `EggIncognito.Tests` | xUnit - unit tests for the generator and `FixtureStore`, plus `WebApplicationFactory` integration tests |
| `EggIncognito.Seeder` | Console app - calls the real Egg Inc API and writes responses as fixture files |
| `EggIncognito.CodeGen` | Code generator - converts fixtures to binary proto and generates mock servers in other languages |

Controllers are generated into `obj/` at build time and are not checked in. To add an endpoint, edit `endpoints.yaml` only:

```yaml
- path: ei/my_endpoint
  requestType: MyRequestType
  responseType: MyResponseType
```

Then `dotnet build`. Add a fixture file under `Fixtures/default/` if a non-empty default response is needed.

## Multi-language mock servers

`EggIncognito.CodeGen` generates complete self-contained mock server projects from the same `endpoints.yaml` and fixture files. Generated servers need no proto library - they serve pre-baked binary fixtures directly.

```sh
dotnet run --project EggIncognito.CodeGen -- generate go --bake
dotnet run --project EggIncognito.CodeGen -- generate python --bake
dotnet run --project EggIncognito.CodeGen -- generate javascript --bake
dotnet run --project EggIncognito.CodeGen -- generate java --bake
dotnet run --project EggIncognito.CodeGen -- generate kotlin --bake
dotnet run --project EggIncognito.CodeGen -- generate ruby --bake
dotnet run --project EggIncognito.CodeGen -- generate csharp --bake
```

Output goes to `generated/<language>/`. Each project includes a README with run instructions. Pre-built branches are linked at the top of this page, or clone directly:

```sh
git clone --branch generated/go --depth 1 https://github.com/DavidArthurCole/EggIncognito egg-inc-mock-go
```

| Flag | Default | Description |
|---|---|---|
| `--output <dir>` | `generated/<language>` | Output directory |
| `--fixtures <path>` | `EggIncognito/Fixtures` | Fixtures root |
| `--port <n>` | `5080` | Port for the generated server |
| `--bake` | off | Bake fixtures before generating |
