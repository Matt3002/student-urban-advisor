# ============================================================================
# Dockerfile dell'API .NET, build multi-stage:
# 1. build & publish con l'SDK; il restore e' separato dalla copia del
#    sorgente per sfruttare la cache dei layer;
# 2. runtime con la sola immagine ASP.NET, in ascolto sulla porta 8080.
# ============================================================================
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY UrbanAdvisor.Api/UrbanAdvisor.Api.csproj UrbanAdvisor.Api/
RUN dotnet restore UrbanAdvisor.Api/UrbanAdvisor.Api.csproj

COPY UrbanAdvisor.Api/ UrbanAdvisor.Api/
RUN dotnet publish UrbanAdvisor.Api/UrbanAdvisor.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080

ENTRYPOINT ["dotnet", "UrbanAdvisor.Api.dll"]
