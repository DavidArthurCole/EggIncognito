# EggIncognito.Tests

The xUnit test project (net10.0).

Part of [EggIncognito](../README.md). Run from the solution root:

```sh
dotnet test EggIncognito.slnx
```

## Coverage

- The [source generator](../EggIncognito.RouteGenerator/README.md), including `RouteSchemaConsistencyTests` (the generator's parser must agree with Core's `RouteCatalog`).
- `EndpointStore` (grouped lookup + path-param fallback) and the overlay/layering (`EndpointStoreLayeringTests`, `FileEndpointSourceTests`, `MergedRouteCatalogTests`, `DynamicMockControllerTests`).
- The extraction pipeline (`EndpointExtractor.ProcessFlow`, HAR import, self-repair).
- The [capture engine](../EggIncognito.Capture/README.md), plus hosted capture (`CaptureAddressStoreTests`, `CaptureCaNotifierTests`).
- The Core tooling services: `PostmanCollection`, `EndpointStatus`, `BlobDecoder`, `ContentRoot`, `AppMode`.
- The [bot](../EggIncognito.Bot/README.md) pure logic (`BotEmbeds`, `ProtoQuery`, `CommandDefinitions`, `CommandParsing`, `CommandSignature`) and the deploy-agent client (`DeployAgentClientTests`).
- Rate limiting (`RateLimitOptions` / `RateLimitKeys`).
- The docs hub registry (`DocRegistryTests`) and the `MarkdownRenderer` (`MarkdownRendererTests`).
- Auth + roles (`AuthModeTests`, `CurrentUserRoleTests`, `ControllerPolicyAttributeTests`, `AdminControllerTests`).
- Proto registry + backfill: version polling, importers, APK extract/version-code, and ARM-split decoding (`Backfill/`, `BackfillApiTests`, `ElgranjeroImporterTests`, `ApkVersionCodeTests`, `Arm64DecoderTests`).
- The proto feed (`FeedDispatcherTests`, `FeedTriggerTests`, `DiscordFeedPayloadTests`).
- Staged protos (`StagedProtoStoreTests`).
- The device farm (`Devices/`: parsing, probe runner, status store, controller, iOS binary puller).
- Mach-O proto / clientVersion extraction (`MachoTextTests`, `MachoClientVersionReaderTests`, `LiveVersionStoreTests`).
- Blazor components via bUnit: `IconComponentTests`, the page tests (`AdminPageTests`, `ImportPageTests`, `DocsHubTests`, `InspectorPageTests`, `CapturePageTests`), and the host shell (`BlazorShellTests`).
- `WebApplicationFactory<Program>` integration tests, including the Hosted-mode gate.

Signing parity is locked by `Build_WrappedWithSalt_CodeMatchesSeederAlgorithm`.

## DB-backed tests

The suite stays DB-free by default. Tests that need real Postgres (no EF test provider per the tests-DB-free repo rule) are marked `[Fact(Skip = "requires Postgres; ...")]`, for example in `StagedProtoStoreTests`, `DeviceStatusStoreTests`, and `CaptureAddressStoreTests`. They are skipped, never run against an in-memory provider.
