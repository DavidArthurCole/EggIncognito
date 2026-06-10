# EggIncognito.Core

The shared class library (net10.0). No web dependency. It holds the service layer and the `Ei.*` proto types.

Part of [EggIncognito](../README.md). Referenced by the [web app](../EggIncognito/README.md), [Capture](../EggIncognito.Capture/README.md), [Data](../EggIncognito.Data/README.md), and [Bot](../EggIncognito.Bot/README.md).

## Contents

| Area | What lives here |
|---|---|
| Services | `EndpointExtractor`, `EndpointStore`, `TransportPipeline`, `RouteCatalog`, `AuxbrainHosts`, `ProtoReflection`, `EndpointStatus`, `BlobDecoder`, `ContentRoot`, `PostmanCollection`, `BuildInfo`, `Redactor`, `RoutesYamlEditor`, `ProtoFraming`, `MarkdownRenderer`, and more |
| Proto types | `Ei.*` message classes, built by Grpc.Tools from `Proto/ei.proto` |

## Proto types

- `Proto/ei.proto` is a frozen checked-in snapshot of the upstream Egg, Inc. schema. There is no in-repo refresh/extraction tooling; a permanent API is planned. Treat it as frozen.
- It is proto2 syntax. Google.Protobuf handles proto2 and proto3 with the same API.
- Codegen: Grpc.Tools with `GrpcServices="None"` (message classes only, no gRPC stubs). Namespace: `Ei`.
- Always use `JsonParser.Default` / `JsonFormatter.Default` for endpoint JSON, not System.Text.Json.

## Key services

- **`EndpointStore`** - loads JSON endpoints at startup and serves them per request. Grouped path lookup with a path-param fallback (walks up segments). When a DB is configured it resolves the scoped DB source per-lookup.
- **`TransportPipeline`** - the single signing authority. Canonical request build (proto -> `AuthenticatedMessage` + hash -> base64 -> form) and response decode. `Decode` handles both response framings (wrapped, optionally compressed; or direct) by trying wrapped first then falling back. The `new TransportPipeline()` env-salt ctor serves non-DI callers.
- **`RouteCatalog`** - the runtime reader of `routes.yaml`. Parses the same file the source generator parses; the two are kept in agreement by `RouteSchemaConsistencyTests`.
- **`AuxbrainHosts`** - the single source of truth for "is this an auxbrain host". Used by the Inspector host allowlist and the capture proxy's selective-decrypt filter.
- **`ProtoReflection`** - runtime descriptors of the `Ei.*` types. `AllMessageTypeNames()` lists top-level message types.
- **`BlobDecoder`**, **`EndpointStatus`**, **`PostmanCollection`** - the pure cores behind the web Tools/Import endpoints.
- **`ContentRoot.Resolve`** - finds the dir holding `RouteMap/` + `Endpoints/` from the app/base dir.
- **`BuildInfo`** - parses the git SHA stamped into the assembly `InformationalVersion` by the `SourceRevisionId` target in the repo-root `Directory.Build.props`. Surfaced by the bot's `/verify`.
- **`MarkdownRenderer`** - the in-house Markdown-to-HTML renderer for the docs hub (HTML-escapes input first, then applies a fixed transform set; allows only http(s)/relative links). Used by the Blazor `<Markdown>` component.

## Extraction pipeline

`EndpointExtractor` is the capture-to-endpoint pipeline: decode, auto-detect type, redact PII, self-repair `routes.yaml`, write the endpoint.

It exposes one per-flow contract:

```
ProcessFlow(url, method, status, requestDataB64, responseBodyB64)
```

Two paths funnel through it, so they cannot diverge:

- The HAR import path (`/api/import/har` -> `RunFromHar`).
- The in-process capture path (the in-app capture proxy, driven by `CaptureSession` in [EggIncognito.Capture](../EggIncognito.Capture/README.md)).

`ForceWriteEndpoint` is the explicit dashboard "Save as endpoint" path: it overwrites and self-registers the route's request/response types in `routes.yaml`, treating an otherwise-ambiguous auto-detect as confirmed because the user chose to save.

Pipeline support types live in `Redactor`, `RoutesYamlEditor` (load-once / save-once editor), and `ExtractorTypes` (`HarCounts` / `HarDirs` / `RequestDecode` / `DecodedEntry` / `SeederConfig`).
