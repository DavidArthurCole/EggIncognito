# EggIncognito.Core

The shared class library (net10.0). No web dependency. It holds the service layer and the `Ei.*` proto types.

Part of [EggIncognito](../README.md). Referenced by the [web app](../EggIncognito/README.md), [Capture](../EggIncognito.Capture/README.md), [Data](../EggIncognito.Data/README.md), and [Bot](../EggIncognito.Bot/README.md).

## Contents

| Area | What lives here |
|---|---|
| Services | `EndpointExtractor`, `EndpointStore`, `TransportPipeline`, `RouteCatalog`, `AuxbrainHosts`, `ProtoReflection`, `EndpointStatus`, `BlobDecoder`, `ContentRoot`, `PostmanCollection`, `BuildInfo`, `Redactor`, `RoutesYamlEditor`, `ProtoFraming`, `MarkdownRenderer`, `WireForensics`, `RinfoHarvester`, `MitmFlowReader`, and more |
| Proto extraction | Carves the proto schema out of shipped mobile binaries: `ArchiveProtoExtractor` (APK/IPA), `MachoProtoExtractor` (iOS Mach-O), `DescriptorProtoCarver` (the format-agnostic core) |
| Proto registry | `CrawlManifestReader` ingests the GitHub-crawl backfill dataset for proto-version staging |
| Devices | Probe, proxy, and CA-install drivers for the physical capture farm (Android + iOS) |
| Proto types | `Ei.*` message classes, built by Grpc.Tools from the proto schema |

## Proto types

- The proto schema is a frozen checked-in snapshot of the upstream Egg, Inc. schema. There is no in-repo refresh/extraction tooling; a permanent API is planned. Treat it as frozen.
- It is proto2 syntax. Google.Protobuf handles proto2 and proto3 with the same API.
- Codegen: Grpc.Tools with `GrpcServices="None"` (message classes only, no gRPC stubs). Namespace: `Ei`.
- Always use `JsonParser.Default` / `JsonFormatter.Default` for endpoint JSON, not System.Text.Json.

## Key services

- **`EndpointStore`** - loads JSON endpoints at startup and serves them per request. Grouped path lookup with a path-param fallback (walks up segments). When a DB is configured it resolves the scoped DB source per-lookup.
- **`TransportPipeline`** - the single signing authority. Canonical request build (proto -> `AuthenticatedMessage` + hash -> base64 -> form) and response decode. `Decode` handles both response framings (wrapped, optionally compressed; or direct) by trying wrapped first then falling back. The `new TransportPipeline()` env-salt ctor serves non-DI callers.
- **`RouteCatalog`** - the runtime reader of the route map. Parses the same source the code generator parses, so the two agree.
- **`AuxbrainHosts`** - the single source of truth for "is this an auxbrain host". Used by the Inspector host allowlist and the capture proxy's selective-decrypt filter.
- **`ProtoReflection`** - runtime descriptors of the `Ei.*` types. `AllMessageTypeNames()` lists top-level message types.
- **`BlobDecoder`**, **`EndpointStatus`**, **`PostmanCollection`** - the pure cores behind the web Tools/Import endpoints.
- **`ContentRoot.Resolve`** - finds the dir holding the route map and endpoint fixtures from the app/base dir.
- **`BuildInfo`** - parses the git SHA stamped into the assembly `InformationalVersion` by the `SourceRevisionId` build target. Surfaced by the bot's `/verify`.
- **`MarkdownRenderer`** - the in-house Markdown-to-HTML renderer for the docs hub (HTML-escapes input first, then applies a fixed transform set; allows only http(s)/relative links). Used by the Blazor `<Markdown>` component.
- **`WireForensics`** - schema-less and schema-aware diagnosis of corrupt proto blobs. Walks raw bytes by wire type without depending on a parse, records the byte offset where structure first breaks, resolves field paths to names when a root type is supplied, and salvages printable-ASCII runs.
- **`RinfoHarvester`** - reads `BasicRequestInfo` (`clientVersion` / `version` / `build` / `platform`) out of a request's already-decoded display JSON. This is the authoritative source for the iOS `clientVersion` and the real build, which the static binary cannot give. Never throws; returns null when there is no usable rinfo.

## Proto extraction

The proto schema ships embedded in the Egg, Inc. native binary as serialized `FileDescriptorProto` blobs. These services carve it back out. The binary is read as bytes and never executed.

- **`DescriptorProtoCarver`** - the format-agnostic core. Locates and emits the embedded descriptors from a raw binary.
- **`ArchiveProtoExtractor`** - takes a mobile app archive (Android APK or iOS IPA, both zips), locates and decompresses the native binary entry (arm64 `.so` preferred, or the IPA Mach-O), then carves. Size-capped against zip bombs because it runs on a public endpoint over attacker-supplied bytes.
- **`MachoProtoExtractor`** - the iOS-facing seam over the carver. Only the first `__TEXT` page of the Mach-O is FairPlay-encrypted, so the descriptor blobs past it carve straight from the raw on-disk binary with no decrypt. Proven across iOS 1.6.3 (2017) through 1.35.8.
- **`MachoClientVersionReader`**, `Arm64Decoder`, `MachoSymbols`, `MachoText`, `ProtoCleanup`, `ProtoDiff` - supporting carve, disasm, and normalization helpers.

## Proto registry / backfill

- **`CrawlManifestReader`** - parses the GitHub-crawl backfill dataset (a zip of `manifest.json` plus `snapshots/*.proto`) into distinct proto states for staging. Dedupes by proto SHA-256, attaches a version only for trusted confidence (`version-file` or `subject`), and stages heuristic (`tree-scan`) rows version-less for manual review.

## Devices (capture farm)

Drivers for the physical device farm: rooted Android and jailbroken iOS phones, auto-probed, proxied, and CA-trusted so their auxbrain traffic decrypts. All are best-effort and never throw; a failure returns `(false, note)`. Core holds the platform-agnostic drivers; the web project orchestrates them.

- **`IProcessRunner`** - the seam over external tools (`adb`, `ssh`, `ideviceinstaller`). Mockable, so the drivers unit-test without real hardware.
- **`IDeviceProbe`** with `AdbDeviceProbe` / `IosDeviceProbe` - read the installed Egg, Inc. version off a device. Android via `adb shell dumpsys package`; iOS via `ideviceinstaller -l -o xml` (`CFBundleShortVersionString` + `CFBundleVersion`).
- **`IDeviceProxyConfigurator`** with `AdbProxyConfigurator` / `IosProxyConfigurator` - point a device's system HTTP proxy at its capture listener and clear it after.
- **`IDeviceCaInstaller`** with `AdbCaInstaller` / `IosCaInstaller` - install and trust the capture root CA on a device so the proxy's MITM TLS is accepted. Android writes the cert into the system trust store on a rooted device, bind-mounting into zygote's mount namespace (PID 1's is SELinux-denied on the farm ROM). iOS inserts a trust row into `TrustStore.sqlite3` over ssh, then restarts `trustd`. Idempotent; called on capture start and whenever the proxy mints a fresh CA.
- **`CaCertPrep`**, **`DeviceParsing`**, **`IDeviceStoreChecker`** - cert encoding (Android subject hash, iOS DER hex), tool-output parsing, and the store-ahead check seam.

## Extraction pipeline

`EndpointExtractor` is the capture-to-endpoint pipeline: decode, auto-detect type, redact PII, self-repair the route map, write the endpoint.

It exposes one per-flow contract:

```
ProcessFlow(url, method, status, requestDataB64, responseBodyB64)
```

Two paths funnel through it, so they cannot diverge:

- The HAR import path (`/api/import/har` -> `RunFromHar`).
- The in-process capture path (the in-app capture proxy, driven by `CaptureSession` in [EggIncognito.Capture](../EggIncognito.Capture/README.md)).

`ForceWriteEndpoint` is the explicit dashboard "Save as endpoint" path: it overwrites and self-registers the route's request/response types in the route map, treating an otherwise-ambiguous auto-detect as confirmed because the user chose to save.

Pipeline support types live in `Redactor`, `RoutesYamlEditor` (load-once / save-once editor), and `ExtractorTypes` (`HarCounts` / `HarDirs` / `RequestDecode` / `DecodedEntry` / `SeederConfig`).
