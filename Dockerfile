# ============================================================================
# STAGE 1 - build & publish
# ============================================================================
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore separato per sfruttare la cache dei layer
COPY UrbanAdvisor.Api/UrbanAdvisor.Api.csproj UrbanAdvisor.Api/
RUN dotnet restore UrbanAdvisor.Api/UrbanAdvisor.Api.csproj

# Copia del resto del sorgente e publish
COPY UrbanAdvisor.Api/ UrbanAdvisor.Api/
RUN dotnet publish UrbanAdvisor.Api/UrbanAdvisor.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

# ============================================================================
# STAGE 2 - runtime
# ============================================================================
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .
COPY UrbanAdvisor.Api/Frontend ./Frontend

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080

ENTRYPOINT ["dotnet", "UrbanAdvisor.Api.dll"]
