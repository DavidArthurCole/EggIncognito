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

## Build and test

```sh
dotnet build
dotnet test
dotnet run --project EggIncognito
```

The source generator runs as part of `dotnet build`. Never edit generated files. They are overwritten every build.

The served Tailwind sheet `EggIncognito/wwwroot/tailwind.css` is a committed artifact; the build does not recompile it. When you change the Tailwind source (`wwwroot/app.tailwind.css` or a Razor class), regenerate and commit the sheet:

```sh
dotnet build -p:BuildTailwindCss=true
```

That opt-in build fetches the standalone Tailwind CLI once into `obj/tailwind/` (gitignored) and recompiles the sheet.

## Adding an endpoint

Edit the route map only. The source generator emits the controller:

```yaml
- path: ei/my_endpoint
  request: MyRequestType
  response: MyResponseType
```

Run `dotnet build`. Add a response file under the endpoints directory for your namespace if you need a non-default response. Never hand-write a controller for a route in the route map. It conflicts with the generated one.

## The proto

The proto schema is a frozen checked-in snapshot of the upstream Egg, Inc. schema. There is no in-repo refresh tooling. Only edit it directly if a proto change is genuinely required, then rebuild. Proto changes can invalidate existing endpoint files.

## Code style

- No em dashes. Use hyphens.
- No banner or divider comments. No runs of 4 or more repeated characters like `====` or `----`.
- No extra spaces to vertically align code or trailing comments. One space before an inline comment.
- Keep comments terse. Explain why, not what. Cut comments that restate the code.
- Match the style of the surrounding file.

These apply to both prose docs and code comments.

## Route generator

Keep `EggIncognito.RouteGenerator` on `netstandard2.0` and dependency-free. Its route-parsing logic is duplicated from the runtime route catalog on purpose and kept in sync automatically. Do not merge them.

## Submitting

- Branch off `main`.
- Keep `dotnet build` at 0 errors and `dotnet test` green.
- Open a pull request describing the change and why.
