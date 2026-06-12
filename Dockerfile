FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore: copy each csproj the web project depends on (Core + Capture + Data + Bot + RouteGenerator)
# so the restore layer caches independently of source changes.
COPY EggIncognito.Core/EggIncognito.Core.csproj EggIncognito.Core/
COPY EggIncognito.Capture/EggIncognito.Capture.csproj EggIncognito.Capture/
COPY EggIncognito.Data/EggIncognito.Data.csproj EggIncognito.Data/
COPY EggIncognito.Bot/EggIncognito.Bot.csproj EggIncognito.Bot/
COPY EggIncognito.RouteGenerator/EggIncognito.RouteGenerator.csproj EggIncognito.RouteGenerator/
COPY EggIncognito/EggIncognito.csproj EggIncognito/
RUN dotnet restore EggIncognito/EggIncognito.csproj

# Fetch the Tailwind CLI before the source COPYs so the (network-bound) download caches independently of
# source edits - only ARG/base-image changes bust it, not a routine code change.
ARG TAILWIND_VERSION=v3.4.17
RUN set -eux; \
    arch="$(uname -m)"; \
    case "$arch" in \
      x86_64) tw=linux-x64 ;; \
      aarch64|arm64) tw=linux-arm64 ;; \
      *) echo "unsupported arch $arch" >&2; exit 1 ;; \
    esac; \
    curl -fsSL \
      "https://github.com/tailwindlabs/tailwindcss/releases/download/${TAILWIND_VERSION}/tailwindcss-${tw}" \
      -o /usr/local/bin/tailwindcss; \
    chmod +x /usr/local/bin/tailwindcss

COPY EggIncognito.Core/ EggIncognito.Core/
COPY EggIncognito.Capture/ EggIncognito.Capture/
COPY EggIncognito.Data/ EggIncognito.Data/
COPY EggIncognito.Bot/ EggIncognito.Bot/
COPY EggIncognito.RouteGenerator/ EggIncognito.RouteGenerator/
COPY EggIncognito/ EggIncognito/

# Compile the Tailwind sheet deterministically here rather than via the MSBuild AfterTargets hook.
# That hook fetches the CLI with ContinueOnError, so a flaky network silently ships an image with no
# wwwroot/tailwind.css - the page then loads completely unstyled (and the static GET 405s through the
# OPTIONS/POST catch-all). Doing it explicitly, with no ContinueOnError, fails the build loud instead.
RUN set -eux; \
    cd EggIncognito; \
    tailwindcss -c tailwind.config.js \
      -i wwwroot/app.tailwind.css \
      -o wwwroot/tailwind.css --minify 2>&1 | tee /tmp/tw.log; \
    grep -q "No utility classes were detected" /tmp/tw.log \
      && { echo "Tailwind scanned no classes - content globs wrong, refusing to ship unstyled" >&2; exit 1; } \
      || true; \
    test -s wwwroot/tailwind.css; \
    grep -q "btn-primary" wwwroot/tailwind.css

# Publish WITH restore: the pre-source restore layer only warms the NuGet cache. Publishing
# --no-restore against that csproj-only restore leaves the static-web-assets manifest stale, which
# silently drops wwwroot/_framework (blazor.web.js) from the output - the Blazor circuit then never
# starts in prod. The re-restore is cheap (packages cached in the layer above).
# EmitTypes=false skips the dashboard-typedef regeneration target. BuildTailwindCss=false skips the
# MSBuild Tailwind hook since the sheet was just compiled above; publish then bundles it as-is.
RUN dotnet publish EggIncognito/EggIncognito.csproj -c Release -o /app/publish \
        -p:EmitTypes=false -p:BuildTailwindCss=false; \
    test -s /app/publish/wwwroot/tailwind.css; \
    test -s /app/publish/wwwroot/_framework/blazor.web.js

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
COPY EggIncognito/Endpoints /app/Endpoints
# RouteMap/routes.yaml is only an AdditionalFiles (compile-time, for the source generator), so it is
# NOT in the publish output. The runtime RouteCatalog reads it from the content root, so it must be
# present next to the app or ContentRoot.Resolve finds no RouteMap/ and the route catalog is empty
# (no endpoints listed, status 0/0/0). Ship it explicitly.
COPY EggIncognito/RouteMap /app/RouteMap

VOLUME ["/app/Endpoints"]

ENV ASPNETCORE_URLS=http://+:8080
ENV EndpointsPath=/app/Endpoints
# Containers have no browser to auto-open.
ENV NoBrowser=true
# The public deploy runs in Hosted mode (capture + endpoint writes disabled - a request must not
# mutate shared data, and the capture proxy/CA cannot be shared). Set AppMode=Hosted at deploy time
# (e.g. compose/k8s env) for the public image. The default image stays Local so a self-host run has
# full features.
# ENV AppMode=Hosted

EXPOSE 8080
EXPOSE 8443
ENTRYPOINT ["dotnet", "EggIncognito.dll"]
