FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy csprojs first so the restore layer caches independently of source.
COPY EggIncognito.Core/EggIncognito.Core.csproj EggIncognito.Core/
COPY EggIncognito.Capture/EggIncognito.Capture.csproj EggIncognito.Capture/
COPY EggIncognito.Data/EggIncognito.Data.csproj EggIncognito.Data/
COPY EggIncognito.Bot/EggIncognito.Bot.csproj EggIncognito.Bot/
COPY EggIncognito.RouteGenerator/EggIncognito.RouteGenerator.csproj EggIncognito.RouteGenerator/
COPY EggIncognito/EggIncognito.csproj EggIncognito/
RUN dotnet restore EggIncognito/EggIncognito.csproj

# Fetch Tailwind CLI before source COPYs so the download caches independently of source edits.
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

# Compile Tailwind here, not via the MSBuild hook: the hook's ContinueOnError can silently ship an
# unstyled image on a flaky network. Explicit + no ContinueOnError fails the build loud instead.
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

# Publish WITH restore: --no-restore against the csproj-only restore leaves the static-web-assets
# manifest stale and drops wwwroot/_framework (blazor.web.js). Re-restore is cheap (cached above).
# EmitTypes=false skips typedef regen. BuildTailwindCss=false skips the hook (sheet compiled above).
RUN dotnet publish EggIncognito/EggIncognito.csproj -c Release -o /app/publish \
        -p:EmitTypes=false -p:BuildTailwindCss=false; \
    test -s /app/publish/wwwroot/tailwind.css; \
    test -s /app/publish/wwwroot/_framework/blazor.web.js

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
# Npgsql/HttpClient dlopen libgssapi_krb5 during OAuth token exchange; aspnet base omits it. Ship it.
# iproute2: hosted capture binds a per-user IPv6 /65 via an AnyIP local route added at startup (the
# host routes the prefix to this container; the container kernel must accept those destinations so the
# front-door socket sees the real per-user dest). proto-extract toolchain NOT baked (~250MB).
# adb + ideviceinstaller: device-status probes shell out to these to read the installed EI version.
# They reach the plugged-in devices via the host's adb server (ADB_SERVER_SOCKET) + usbmuxd socket
# (mounted at runtime), not raw USB. Without them every probe reports the device unreachable.
# openssh-client: iOS proto extraction (IosBinaryPuller) + the auto-update trigger (IosDeviceUpdater)
# shell out to ssh/scp to reach the jailbroken phone over LAN. Without it the pull fails "ssh: not found".
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 iproute2 adb ideviceinstaller openssh-client \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish .
COPY EggIncognito/Endpoints /app/Endpoints
# routes.yaml is AdditionalFiles (compile-time only), not in publish output. Runtime RouteCatalog
# reads it from the content root; without it the catalog is empty (0/0/0). Ship it explicitly.
COPY EggIncognito/RouteMap /app/RouteMap
COPY docker-entrypoint.sh /usr/local/bin/docker-entrypoint.sh
RUN chmod +x /usr/local/bin/docker-entrypoint.sh

VOLUME ["/app/Endpoints"]

ENV ASPNETCORE_URLS=http://+:8080
ENV EndpointsPath=/app/Endpoints
# Containers have no browser to auto-open.
ENV NoBrowser=true
# Public deploy runs Hosted (capture + writes disabled). Set AppMode=Hosted at deploy time for the
# public image. Default stays Local so self-host has full features.
# ENV AppMode=Hosted

# Device probes: the container's adb talks to the HOST's adb server rather than spawning its own (a
# container-local server sees no USB). Host server listens 127.0.0.1:5037, reachable only when the
# container shares the host network namespace (network_mode: host). usbmuxd is reached via the mounted
# socket. Both are wired at deploy time (compose/Portainer), see docker-compose.yml.
ENV ADB_SERVER_SOCKET=tcp:127.0.0.1:5037

EXPOSE 8080
EXPOSE 8443
ENTRYPOINT ["/usr/local/bin/docker-entrypoint.sh"]
