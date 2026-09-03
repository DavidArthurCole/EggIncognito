# EggIncognito.RouteGenerator

The Roslyn incremental source generator (netstandard2.0). Reads the route map and emits one mock controller per endpoint at build time, as part of `dotnet build` for the [web app](../EggIncognito/README.md). Generated controllers land in build output and are not checked in.

It ships as an analyzer assembly the compiler loads, so it carries no runtime or project references; the only package references are the Roslyn compiler APIs themselves. That is why the route-parsing logic is deliberately duplicated from the runtime route catalog rather than shared: the two parsers are kept in sync instead of merged.
