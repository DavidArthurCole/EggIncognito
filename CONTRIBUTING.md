# Contributing to EggIncognito

Thanks for helping out. This covers how to build, test, and submit changes.

## Layout

EggIncognito is a .NET solution split into focused projects. Each has its own README:

- [EggIncognito](EggIncognito/README.md) - the ASP.NET Core web app.
- [EggIncognito.Core](EggIncognito.Core/README.md) - shared services + the `Ei.*` proto types.
- [EggIncognito.Capture](EggIncognito.Capture/README.md) - the capture engine.
- [EggIncognito.Data](EggIncognito.Data/README.md) - the optional Postgres layer.
- [EggIncognito.Bot](EggIncognito.Bot/README.md) - the optional Discord bot.
- [EggIncognito.RouteGenerator](EggIncognito.RouteGenerator/README.md) - the Roslyn source generator.
- [EggIncognito.Tests](EggIncognito.Tests/README.md) - the test suite.

## Build and test

```sh
dotnet build EggIncognito.slnx
dotnet test EggIncognito.slnx
dotnet run --project EggIncognito
```

The source generator and the Tailwind compile run as part of `dotnet build`. Never edit generated files in `obj/`. They are overwritten every build.

When you change only `wwwroot/**` or a `.razor` component and need the Tailwind sheet recompiled, run:

```sh
dotnet build EggIncognito/EggIncognito.csproj -t:BuildTailwind
```

The compile target runs `AfterTargets="Build"`, so a wwwroot-only incremental build skips it otherwise.

## Adding an endpoint

Edit `EggIncognito/RouteMap/routes.yaml` only. The source generator emits the controller:

```yaml
- path: ei/my_endpoint
  request: MyRequestType
  response: MyResponseType
```

Run `dotnet build`. Add a response file under `EggIncognito/Endpoints/default/<namespace>/` if you need a non-default response. Never hand-write a controller for a route in `routes.yaml`. It conflicts with the generated one.

## The proto

`EggIncognito.Core/Proto/ei.proto` is a frozen checked-in snapshot of the upstream Egg, Inc. schema. There is no in-repo refresh tooling. Only edit it directly if a proto change is genuinely required, then rebuild. Proto changes can invalidate existing endpoint files.

## Code style

- No em dashes. Use hyphens.
- No banner or divider comments. No runs of 4 or more repeated characters like `====` or `----`.
- No extra spaces to vertically align code or trailing comments. One space before an inline comment.
- Keep comments terse. Explain why, not what. Cut comments that restate the code.
- Match the style of the surrounding file.

These apply to both prose docs and code comments.

## Tests

Add tests for new behavior. The suite is xUnit. Pure logic should be unit-tested directly; web behavior is covered by `WebApplicationFactory<Program>` integration tests. See the [test project README](EggIncognito.Tests/README.md).

Keep `EggIncognito.RouteGenerator` on `netstandard2.0` and dependency-free. Its route-parsing logic is duplicated from `Core/Services/RouteCatalog.cs` on purpose and kept in sync by `RouteSchemaConsistencyTests`. Do not merge them.

## Submitting

- Branch off `main`.
- Keep `dotnet build` at 0 errors and `dotnet test EggIncognito.slnx` green.
- Open a pull request describing the change and why.
