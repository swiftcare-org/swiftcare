# Context is the repository root, so COPY paths are repo-relative and scoped per project
# to keep the build cache from busting on unrelated changes.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

COPY services/QueueService/QueueService.csproj services/QueueService/
RUN dotnet restore services/QueueService/QueueService.csproj

COPY services/QueueService/ services/QueueService/
RUN dotnet publish services/QueueService/QueueService.csproj \
    --no-restore \
    --configuration Release \
    --output /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

# curl is not in the runtime image and the HEALTHCHECK below needs it.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_HTTP_PORTS=5003
EXPOSE 5003

# /health is exempt from the gateway-secret check, so Compose can probe the worker.
HEALTHCHECK --interval=10s --timeout=5s --retries=10 --start-period=30s \
    CMD curl --fail --silent http://localhost:5003/health || exit 1

USER $APP_UID
ENTRYPOINT ["dotnet", "QueueService.dll"]
