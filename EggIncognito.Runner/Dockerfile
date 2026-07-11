# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
ENV NUGET_PACKAGES=/root/.nuget/packages

COPY nuget.config ./
COPY EggIncognito.Core/EggIncognito.Core.csproj EggIncognito.Core/
COPY EggIncognito.Runner/EggIncognito.Runner.csproj EggIncognito.Runner/
# EggIncognito.Core depends on SyncKit.Contract from GitHub Packages; needs auth even for public packages.
ARG GITHUB_PACKAGES_USER
RUN --mount=type=secret,id=github_token \
    --mount=type=cache,id=nuget-packages,target=/root/.nuget/packages \
    dotnet nuget update source github \
      --username "$GITHUB_PACKAGES_USER" \
      --password "$(cat /run/secrets/github_token)" \
      --store-password-in-clear-text \
      --configfile nuget.config \
    && dotnet restore EggIncognito.Runner/EggIncognito.Runner.csproj

COPY EggIncognito.Core/ EggIncognito.Core/
COPY EggIncognito.Runner/ EggIncognito.Runner/

RUN --mount=type=cache,id=nuget-packages,target=/root/.nuget/packages \
    dotnet publish EggIncognito.Runner/EggIncognito.Runner.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
# adb only: android polling reaches the host's adb server via ADB_SERVER_SOCKET (network_mode: host
# at deploy time), no daemon in this container. iOS path reads a pre-staged binary (IOS_BINARY_PATH)
# pulled by the main eggincognito container's IosBinaryPuller over ssh, so no ssh/ideviceinstaller here.
RUN apt-get update \
    && apt-get install -y --no-install-recommends adb \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish .

ENV ADB_SERVER_SOCKET=tcp:127.0.0.1:5037

ENTRYPOINT ["dotnet", "EggIncognito.Runner.dll"]
