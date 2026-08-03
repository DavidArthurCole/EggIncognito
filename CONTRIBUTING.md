# Contributing

## Build and test

```sh
dotnet build
dotnet test
dotnet run --project EggIncognito
```

Codegen runs during `dotnet build`: controllers are generated from the route map, and the served stylesheet is compiled from the CSS source in pure C#. Generated files are overwritten every build; edit the sources, not the output.

## Adding an endpoint

Edit the route map only, then rebuild:

```yaml
- path: ei/my_endpoint
  request: MyRequestType
  response: MyResponseType
```

Add a response file under the endpoints directory if a non-default response is needed. Hand-written controllers conflict with the generated ones.

## The proto

The proto schema is a frozen checked-in snapshot of the upstream Egg, Inc. schema. Only edit it if a proto change is genuinely required, then rebuild; proto changes can invalidate existing endpoint files.

## Submitting

- Branch off `main`.
- Keep `dotnet build` at 0 errors and `dotnet test` green.
- Open a pull request describing the change and why.
