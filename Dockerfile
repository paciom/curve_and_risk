# syntax=docker/dockerfile:1

# ---- Build ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

# Build configuration first, then the projects the API needs. Tests, evals and the MCP server are not
# part of the image; .dockerignore keeps them and everything else out of the build context.
COPY global.json Directory.Build.props Directory.Build.targets Directory.Packages.props ./
COPY .editorconfig BannedSymbols.txt CodeMetricsConfig.txt ./
COPY src/ src/

# Analyzers are the quality gate's job and have already passed in CI for any commit that reaches this
# build; running them again here would only make the image slower to build.
RUN dotnet publish src/CurveRisk.Api/CurveRisk.Api.csproj \
      --configuration Release \
      --output /app \
      -p:RunAnalyzers=false \
      -p:EnforceCodeStyleInBuild=false \
      -p:UseAppHost=false

# ---- Run ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# The local database lives on a volume so it survives the container. With Database__Provider=Postgres
# and ConnectionStrings__CurveRisk set, the volume is unused.
RUN mkdir /data && chown "$APP_UID" /data
VOLUME /data

ENV ASPNETCORE_HTTP_PORTS=8080 \
    Database__Provider=Sqlite \
    ConnectionStrings__CurveRisk="Data Source=/data/curverisk.db"

COPY --from=build /app ./

# The base image defines an unprivileged user; nothing here needs root.
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "CurveRisk.Api.dll"]
