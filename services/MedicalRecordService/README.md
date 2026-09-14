# MedicalRecordService

MedicalRecordService owns consultation records and consultation templates. The SWC-24 implementation uses parameterized ADO.NET commands through `MySqlConnector` and accepts requests only after the API Gateway establishes its internal trust boundary.

## Port

`5004` (see `Properties/launchSettings.json`).

## Endpoints

| Method | Path | Auth | Description |
| --- | --- | --- | --- |
| `GET` | `/health` | none | Health check |
| `GET` | `/api/templates` | Doctor | Returns active consultation templates ordered by name |
| `POST` | `/api/consultations` | Doctor | Creates a consultation for the doctor's current queue assignment |

The Gateway supplies the authenticated doctor's ID, name, and room number. These values are not accepted from the request body. Symptoms and diagnosis are required; examination findings, notes, and template selection are optional.

## Database schema

The service schema is defined in `Database/schema.sql`. It creates the `ConsultationTemplates` and `Consultations` tables and seeds general, respiratory, gastrointestinal, and musculoskeletal consultation templates.

With the repository environment variables loaded and the MySQL container running, apply it from the repository root:

```powershell
Get-Content -Raw services/MedicalRecordService/Database/schema.sql |
    docker compose exec -T mysql mysql "-u$env:MYSQL_USER" "-p$env:MYSQL_PASSWORD" $env:MEDICAL_RECORD_DB_NAME
```

Each queue entry can have at most one consultation record. The chosen template ID and name are stored on the consultation so the template used for the visit remains identifiable.

## Required environment variables

| Variable | Purpose |
| --- | --- |
| `ConnectionStrings__MedicalRecordDb` | MySQL connection string for `swiftcare_medical_record` |
| `Gateway__InternalSecret` | Shared secret that must match the API Gateway |
| `ASPNETCORE_ENVIRONMENT` | Use `Development` locally to expose OpenAPI and Scalar |

Example local configuration:

```powershell
$env:ConnectionStrings__MedicalRecordDb = "Server=localhost;Port=3306;Database=$env:MEDICAL_RECORD_DB_NAME;User Id=$env:MYSQL_USER;Password=$env:MYSQL_PASSWORD;"
$env:Gateway__InternalSecret = $env:GATEWAY_INTERNAL_SECRET
$env:ASPNETCORE_ENVIRONMENT = "Development"
```

## Running locally

```powershell
dotnet run --project services/MedicalRecordService/MedicalRecordService.csproj
```

OpenAPI is available at `/openapi/v1.json` in Development. Scalar is available at `/scalar/v1`.

## Testing

```powershell
dotnet test tests/MedicalRecordService.UnitTests/MedicalRecordService.UnitTests.csproj
```

The tests cover consultation creation and identity linkage, template prefill data, required-field validation, duplicate queue protection, role enforcement, and Gateway-secret enforcement.
