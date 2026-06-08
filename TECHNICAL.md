Internals and reference for [EggIncognito](README.md). For the capture walkthrough see [CAPTURE.md](CAPTURE.md).

## Wire format

- Request: `POST /<path>`, body `data=<base64(proto bytes)>`, content type `application/x-www-form-urlencoded`.
- Response: base64-encoded proto bytes. Content-Type is `text/html` for success (2xx) and `text/plain` for errors.
- The real Egg, Inc. API wraps many responses in an `AuthenticatedMessage` envelope (optionally gzip/zlib/deflate compressed); the mock returns the inner response message directly. Signed requests wrap the inner proto in an `AuthenticatedMessage` with a SHA256-based `code`.

## Architecture

| Project | Purpose |
|---|---|
| `EggIncognito` | ASP.NET Core (net10.0) - the single app: mock controllers, the Inspector (`/inspector/`) and capture (`/capture/`) SPAs + their controllers, and the CLI subcommands in `Cli/` (`emit-types`, `check-endpoints`, `export-collection`). |
| `EggIncognito.Core` | Class lib (net10.0) - the shared library code (`Services/EndpointExtractor.cs`, `Services/EndpointStore.cs`, `Services/TransportPipeline.cs`, `Services/RouteCatalog.cs`, `Services/AuxbrainHosts.cs`, etc.) plus the `Ei.*` proto types (`Proto/ei.proto` built by Grpc.Tools). No web dependency. |
| `EggIncognito.Capture` | Class lib (net10.0) - the capture engine: `UnobtaniumCaptureProxy` (selective-decrypt of auxbrain only), `CaptureHub`/`FlowProcessor`/`FlowDecoder`/`HarWriter`, and `CaptureSession` (start/stop lifecycle owner). Refs Core. See [CAPTURE.md](CAPTURE.md). |
| `EggIncognito.RouteGenerator` | Roslyn `IIncrementalGenerator` (netstandard2.0) - reads `routes.yaml` and emits one controller per endpoint at build time. |
| `EggIncognito.Tests` | xUnit - unit tests for the generator, `EndpointStore`, the extraction pipeline, capture, the CLI subcommands, and `WebApplicationFactory` integration tests. |

Controllers are generated into `obj/` at build time and are NOT checked in (never edit generated files). To add an endpoint, edit `routes.yaml` only:

```yaml
- path: ei/my_endpoint
  request: MyRequestType
  response: MyResponseType
```

Then run `dotnet build`. Add an endpoint file under `EggIncognito/Endpoints/default/<namespace>/` if a non-default response is needed.

The extraction pipeline lives in `EggIncognito.Core/Services/EndpointExtractor.cs` and exposes one per-flow method `ProcessFlow(url, method, status, requestDataB64, responseBodyB64)`. Both the HAR replay path (`from-har` subcommand) and the in-process capture path (the in-app capture proxy, driven by `CaptureSession`) funnel through it, so they cannot diverge.

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
| `EGG_INC_EID` | required for live seeding | EID for the `seed` live-API mode and the in-app capture default |
| `CapturePort` | `8080` | Port the capture proxy listens on |
| `CapturePath` | `<repo>/captures` | Directory the capture HAR is written to |
| `CaPath` | `<CapturePath>/eggincognito-ca.cer` | The persisted capture root CA file |
| `EGG_INC_API_SALT` | required for live seeding | API signing phrase for the `seed` live-API mode |

## Maintenance subcommands

The former PowerShell maintenance scripts and the standalone Seeder exe are now subcommands of the
unified `EggIncognito` CLI, dispatched before the web host boots. The proto-refresh, APK-diff,
mitmproxy-extract, and publish scripts have been removed.

| Command | Purpose |
|---|---|
| `dotnet run --project EggIncognito -- export-collection [--output <path>]` | Generate a Postman v2.1 collection from `routes.yaml` |
| `dotnet run --project EggIncognito -- check-endpoints [--update]` | Report routes with a missing or empty endpoint; `--update` rewrites the `endpoint_status:` block |
| `dotnet run --project EggIncognito -- emit-types` | Regenerate `wwwroot/capture/types.d.ts` from the capture records |
| `dotnet run --project EggIncognito -- seed` | Live-API batch seed into `Endpoints/eids/<EID>/` (needs `EGG_INC_EID` + `EGG_INC_API_SALT`) |
| `dotnet run --project EggIncognito -- from-har <file> [--overwrite]` | Replay a HAR into `Endpoints/default/` (staged for review unless `--overwrite`) |
| `dotnet run --project EggIncognito -- decode <base64>` | Auto-detect the proto type of an arbitrary blob |
