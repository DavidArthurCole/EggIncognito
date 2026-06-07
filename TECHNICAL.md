Internals and reference for [EggIncognito](README.md). For the capture walkthrough see [CAPTURE.md](CAPTURE.md).

## Wire format

- Request: `POST /<path>`, body `data=<base64(proto bytes)>`, content type `application/x-www-form-urlencoded`.
- Response: base64-encoded proto bytes. Content-Type is `text/html` for success (2xx) and `text/plain` for errors.
- The real Egg, Inc. API wraps many responses in an `AuthenticatedMessage` envelope (optionally gzip/zlib/deflate compressed); the mock returns the inner response message directly. Signed requests wrap the inner proto in an `AuthenticatedMessage` with a SHA256-based `code`.

## Architecture

| Project | Purpose |
|---|---|
| `EggIncognito` | ASP.NET Core (net10.0) - the mock server: controllers, endpoints, startup. Also hosts the shared library code (`Services/EndpointExtractor.cs`, `Services/EndpointStore.cs`, `Services/TransportPipeline.cs`, `Services/AuxbrainHosts.cs`) used by the other tools. |
| `EggIncognito.Generator` | Roslyn `IIncrementalGenerator` (netstandard2.0) - reads `routes.yaml` and emits one controller per endpoint at build time. |
| `EggIncognito.Tests` | xUnit - unit tests for the generator, `EndpointStore`, the extraction pipeline, and `WebApplicationFactory` integration tests. |
| `EggIncognito.Seeder` | Console app - a thin CLI over the extraction pipeline. Calls the real Egg, Inc. API to seed per-EID endpoints, replays a HAR (`--from-har`), or identifies an unknown proto blob (`--decode`). |
| `EggIncognito.Tooling` | Console app - the pure-C# capture proxy (`capture` command). Selective-decrypts only auxbrain hosts, feeds captured flows to `EndpointExtractor` in-process, and writes a HAR. See [CAPTURE.md](CAPTURE.md). |

Controllers are generated into `obj/` at build time and are NOT checked in (never edit generated files). To add an endpoint, edit `routes.yaml` only:

```yaml
- path: ei/my_endpoint
  request: MyRequestType
  response: MyResponseType
```

Then run `dotnet build`. Add an endpoint file under `EggIncognito/Endpoints/default/<namespace>/` if a non-default response is needed.

The extraction pipeline lives in `EggIncognito/Services/EndpointExtractor.cs` and exposes one per-flow method `ProcessFlow(url, method, status, requestDataB64, responseBodyB64)`. Both the HAR replay path (Seeder `--from-har`) and the in-process capture path (Tooling `capture`) funnel through it, so they cannot diverge.

## Distribution

The app ships two ways, both produced by the `Release` workflow on a `v*` tag:

- **Self-contained binaries** - `dotnet publish --self-contained -p:PublishSingleFile=true` per
  runtime (`linux-x64`, `linux-arm64`, `osx-arm64`, `win-x64`). No .NET install needed; the
  `Endpoints/` response set is bundled alongside the executable. Attached to the GitHub release.
- **Container image** - `ghcr.io/davidarthurcole/eggincognito:<tag>` and `:latest`, built from the
  repo `Dockerfile`.

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
| `EGG_INC_EID` | required for live seeding | EID for the Seeder live-API mode and the Tooling capture default |
| `EGG_INC_API_SALT` | required for live seeding | API signing phrase for the Seeder live-API mode |

## Maintenance scripts

Two PowerShell Core scripts (run with `pwsh`) remain, both being ported to the unified `EggIncognito` CLI and slated for removal. The proto-refresh, APK-diff, mitmproxy-extract, and publish scripts have been removed; HAR extraction now goes through `EggIncognito.Seeder -- --from-har` (see [CAPTURE.md](CAPTURE.md)).

| Script | Purpose |
|---|---|
| `Export-Collection.ps1` | Generate a Postman v2.1 collection from `routes.yaml` |
| `Check-Endpoints.ps1` | Report routes with a missing or empty endpoint |
