FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY services/MedicalRecordService/MedicalRecordService.csproj services/MedicalRecordService/
RUN dotnet restore services/MedicalRecordService/MedicalRecordService.csproj
COPY services/MedicalRecordService/ services/MedicalRecordService/
RUN dotnet publish services/MedicalRecordService/MedicalRecordService.csproj --no-restore --configuration Release --output /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=5004
EXPOSE 5004
HEALTHCHECK --interval=10s --timeout=5s --retries=10 --start-period=30s \
    CMD curl --fail --silent http://localhost:5004/health || exit 1
USER $APP_UID
ENTRYPOINT ["dotnet", "MedicalRecordService.dll"]
