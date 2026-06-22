# EggIncognito.RouteGenerator

The Roslyn `IIncrementalGenerator` (netstandard2.0). Reads `routes.yaml` and emits one mock controller per endpoint at build time.

Part of [EggIncognito](../README.md). The generator runs automatically as part of `dotnet build` for the [web app](../EggIncognito/README.md).

## What it does

- Reads `EggIncognito/RouteMap/routes.yaml`.
- Emits one controller class per route entry.
- Path-parameterized routes (`pathParam: true`) get a second `[HttpPost("{param}")]` action. `pathParamOnly: true` means there is no request body.
- `requestWrapped` / `responseWrapped` flag whether the proto rides inside an `AuthenticatedMessage` envelope on the wire (request wrapped = signed; response wrapped = unwrap to inner).

Generated controllers land in `obj/`. They are not checked in. Never edit them; they are overwritten on every build. Never hand-write a controller for a route in `routes.yaml` - it conflicts with the generated code.

To add an endpoint, edit `routes.yaml` and rebuild. See the [web app README](../EggIncognito/README.md#adding-an-endpoint).

## Constraints

- Must stay on `netstandard2.0`. Never change the target framework.
- Must stay dependency-free. Never add runtime dependencies.
- `RouteParser` / `RouteModel` are deliberately NOT shared with `EggIncognito.Core/Services/RouteCatalog.cs`. The generator must stay netstandard2.0 + dependency-free, so the parse logic is duplicated. The two are kept in sync by `RouteSchemaConsistencyTests` in [EggIncognito.Tests](../EggIncognito.Tests/README.md). Do not try to merge it into Core.
- `RouteModel.cs` supports the legacy `requestType:` / `responseType:` aliases and the `*Wrapped` flags, matching the runtime `RouteCatalog`.
