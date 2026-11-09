# syntax=docker/dockerfile:1

# ---- build: restore (cached on project files only), then compile ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY global.json Directory.Build.props TransactionsDisputePortal.slnx dotnet-tools.json ./
COPY src/DisputePortal.Domain/DisputePortal.Domain.csproj src/DisputePortal.Domain/
COPY src/DisputePortal.Application/DisputePortal.Application.csproj src/DisputePortal.Application/
COPY src/DisputePortal.Infrastructure/DisputePortal.Infrastructure.csproj src/DisputePortal.Infrastructure/
COPY src/DisputePortal.Web/DisputePortal.Web.csproj src/DisputePortal.Web/
COPY tests/DisputePortal.UnitTests/DisputePortal.UnitTests.csproj tests/DisputePortal.UnitTests/
COPY tests/DisputePortal.IntegrationTests/DisputePortal.IntegrationTests.csproj tests/DisputePortal.IntegrationTests/
RUN dotnet restore TransactionsDisputePortal.slnx

COPY src/ src/
COPY tests/ tests/
RUN dotnet build TransactionsDisputePortal.slnx -c Release --no-restore

# ---- test: `docker build --target test .` runs the whole suite inside the container ----
FROM build AS test
RUN dotnet test TransactionsDisputePortal.slnx -c Release --no-build

# ---- publish ----
FROM build AS publish
RUN dotnet publish src/DisputePortal.Web/DisputePortal.Web.csproj -c Release --no-build -o /app/publish /p:UseAppHost=false

# ---- runtime: small ASP.NET image, non-root user ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# The SQLite database and data-protection keys live in /app/data. Mount a volume there to keep them.
RUN mkdir -p /app/data/keys && chown -R $APP_UID /app/data
VOLUME /app/data

# Jwt__SigningKey is deliberately NOT set here: secrets never go in an image. Pass it at runtime.
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    ConnectionStrings__DefaultConnection="Data Source=/app/data/disputeportal.db" \
    DataProtection__KeysPath=/app/data/keys \
    Logging__Console__FormatterName=json

COPY --from=publish /app/publish .

USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "DisputePortal.Web.dll"]
