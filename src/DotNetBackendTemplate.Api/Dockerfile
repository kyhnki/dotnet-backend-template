FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

COPY Directory.Build.props Directory.Packages.props BannedSymbols.txt .editorconfig ./
COPY src/DotNetBackendTemplate.Api/DotNetBackendTemplate.Api.csproj src/DotNetBackendTemplate.Api/
RUN dotnet restore src/DotNetBackendTemplate.Api/DotNetBackendTemplate.Api.csproj

COPY src/DotNetBackendTemplate.Api/ src/DotNetBackendTemplate.Api/
RUN dotnet publish src/DotNetBackendTemplate.Api/DotNetBackendTemplate.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "DotNetBackendTemplate.Api.dll"]
