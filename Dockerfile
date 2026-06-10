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

COPY EggIncognito.Core/ EggIncognito.Core/
COPY EggIncognito.Capture/ EggIncognito.Capture/
COPY EggIncognito.Data/ EggIncognito.Data/
COPY EggIncognito.Bot/ EggIncognito.Bot/
COPY EggIncognito.RouteGenerator/ EggIncognito.RouteGenerator/
COPY EggIncognito/ EggIncognito/
# EmitTypes=false skips the dashboard-typedef regeneration target (the committed types.d.ts is
# authoritative); no need to spawn the just-built app during the image build.
RUN dotnet publish EggIncognito/EggIncognito.csproj -c Release -o /app/publish -p:EmitTypes=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
COPY EggIncognito/Endpoints /app/Endpoints

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
