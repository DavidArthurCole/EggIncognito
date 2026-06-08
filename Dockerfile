FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore: copy each csproj the web project depends on (Core + Capture + RouteGenerator) so the
# restore layer caches independently of source changes.
COPY EggIncognito.Core/EggIncognito.Core.csproj EggIncognito.Core/
COPY EggIncognito.Capture/EggIncognito.Capture.csproj EggIncognito.Capture/
COPY EggIncognito.RouteGenerator/EggIncognito.RouteGenerator.csproj EggIncognito.RouteGenerator/
COPY EggIncognito/EggIncognito.csproj EggIncognito/
RUN dotnet restore EggIncognito/EggIncognito.csproj

COPY EggIncognito.Core/ EggIncognito.Core/
COPY EggIncognito.Capture/ EggIncognito.Capture/
COPY EggIncognito.RouteGenerator/ EggIncognito.RouteGenerator/
COPY EggIncognito/ EggIncognito/
RUN dotnet publish EggIncognito/EggIncognito.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
COPY EggIncognito/Endpoints /app/Endpoints

VOLUME ["/app/Endpoints"]

ENV ASPNETCORE_URLS=http://+:8080
ENV EndpointsPath=/app/Endpoints

EXPOSE 8080
EXPOSE 8443
ENTRYPOINT ["dotnet", "EggIncognito.dll"]
