FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY services/NotificationService/NotificationService.csproj services/NotificationService/
RUN dotnet restore services/NotificationService/NotificationService.csproj
COPY services/NotificationService/ services/NotificationService/
RUN dotnet publish services/NotificationService/NotificationService.csproj --no-restore --configuration Release --output /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
RUN apt-get update \
    && apt-get upgrade -y \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=5005
EXPOSE 5005
HEALTHCHECK --interval=10s --timeout=5s --retries=10 --start-period=30s \
    CMD curl --fail --silent http://localhost:5005/health || exit 1
USER $APP_UID
ENTRYPOINT ["dotnet", "NotificationService.dll"]
