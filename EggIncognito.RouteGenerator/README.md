# EggIncognito.RouteGenerator

The Roslyn incremental source generator (netstandard2.0, dependency-free by design). Reads the route map and emits one mock controller per endpoint at build time, as part of `dotnet build` for the [web app](../EggIncognito/README.md). Generated controllers land in build output and are not checked in.

The route-parsing logic is deliberately duplicated from the runtime route catalog: the generator cannot take a project reference without dragging in dependencies, so the two parsers are kept in sync instead of merged.
