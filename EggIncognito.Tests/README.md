# EggIncognito.Tests

The xUnit test project (net10.0).

Part of [EggIncognito](../README.md). Run from the solution root:

```sh
dotnet test EggIncognito.slnx
```

## Coverage

- The [source generator](../EggIncognito.RouteGenerator/README.md), including `RouteSchemaConsistencyTests` (the generator's parser must agree with Core's `RouteCatalog`).
- `EndpointStore` (grouped lookup + path-param fallback).
- The extraction pipeline (`EndpointExtractor.ProcessFlow`, HAR import, self-repair).
- The [capture engine](../EggIncognito.Capture/README.md).
- The Core tooling services: `PostmanCollection`, `EndpointStatus`, `BlobDecoder`, `ContentRoot`, `AppMode`.
- The [bot](../EggIncognito.Bot/README.md) pure logic (`BotEmbeds`, `ProtoQuery`, `CommandDefinitions`).
- Rate limiting (`RateLimitOptions` / `RateLimitKeys`).
- The docs hub registry (`DocRegistryTests`) and the `MarkdownRenderer` (`MarkdownRendererTests`).
- Blazor components via bUnit: `IconComponentTests`, the page tests (`AdminPageTests`, `ImportPageTests`, `DocsHubTests`, `InspectorPageTests`, `CapturePageTests`), and the host shell (`BlazorShellTests`).
- `WebApplicationFactory<Program>` integration tests, including the Hosted-mode gate.

Signing parity is locked by `Build_WrappedWithSalt_CodeMatchesSeederAlgorithm`.
