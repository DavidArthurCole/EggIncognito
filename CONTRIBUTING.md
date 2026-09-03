# Contributing

## Build and test

```sh
dotnet build
dotnet test
dotnet run --project EggIncognito
```

Codegen runs during `dotnet build`: controllers are generated from the route map, and the served stylesheet is compiled from the CSS source in pure C#. Generated files are overwritten every build; edit the sources, not the output.

## Adding an endpoint

Endpoint controllers are generated from the route map at build time, so a route is added by editing the map and rebuilding, never by writing a controller: a hand-written one conflicts with the generated one. Add a response file under the endpoints directory only when the all-default response is not enough.

## The proto

The proto schema is a frozen checked-in snapshot of the upstream Egg, Inc. schema. Only edit it if a proto change is genuinely required, then rebuild; proto changes can invalidate existing endpoint files.

## Submitting

- Branch off `main`.
- Keep `dotnet build` at 0 errors and `dotnet test` green.
- Open a pull request describing the change and why.
