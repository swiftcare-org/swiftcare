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

## EF Core migrations

`MedicalRecordDbContext` and the committed files under `Migrations/` manage the `ConsultationTemplates` and `Consultations` tables. The initial migration seeds the general, respiratory, gastrointestinal, and musculoskeletal templates with stable GUIDs. The normal MedicalRecordService application image supports both API and migration execution; no separate migration image is required.

To apply migrations with the normal service image:

```powershell
docker compose run --rm --no-deps medicalrecordservice --migrate
```

Docker Compose, CI, and Azure migration-job orchestration are tracked separately under SWC-108.

To run it directly from the repository, configure the connection and execute the maintenance command:

```powershell
$env:ConnectionStrings__MedicalRecordDb = "Server=localhost;Port=3306;Database=swiftcare_medical_record;User Id=<MYSQL_USER>;Password=<MYSQL_PASSWORD>;"
dotnet run --project services/MedicalRecordService -- --migrate
```

The process exits with a non-zero code if it cannot connect or apply a migration. Running it again after all migrations have been applied succeeds without recreating tables or duplicating templates.

### Replacing a database created by `schema.sql`

Automatic baselining of the retired SQL-created schema is not supported. The development MedicalRecord database is disposable and must be recreated before applying the initial EF migration:

1. Confirm that no MedicalRecord data must be retained. Export a backup if there is any doubt.
2. Stop MedicalRecordService so the database receives no writes.
3. Drop and recreate only the `swiftcare_medical_record` development database. Do not remove any other service database.
4. Restore the configured MedicalRecord database user's privileges on the recreated database if required.
5. Run the normal MedicalRecordService image with `--migrate`.
6. Confirm that `ConsultationTemplates`, `Consultations`, and `__EFMigrationsHistory` exist and that the four templates were seeded.
7. Run `--migrate` a second time and confirm that no migrations are pending.
8. Start MedicalRecordService and verify `/health`, template retrieval, and consultation creation.

Do not manually insert rows into `__EFMigrationsHistory`. Running `--migrate` against an existing SQL-created schema fails instead of silently adopting it.

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

The unit tests cover consultation creation and identity linkage, template prefill data, required-field validation, duplicate queue protection, role enforcement, Gateway-secret enforcement, and maintenance-command parsing.

Migration compatibility tests require a disposable MySQL 8.4 instance and a test account allowed to create and drop databases. Never point this variable at a shared or production database:

```powershell
$env:MEDICAL_RECORD_MIGRATION_TEST_CONNECTION = "Server=localhost;Port=3306;Database=mysql;User Id=root;Password=<LOCAL_TEST_PASSWORD>;"
dotnet test tests/MedicalRecordService.MigrationTests/MedicalRecordService.MigrationTests.csproj
Remove-Item Env:\MEDICAL_RECORD_MIGRATION_TEST_CONNECTION
```

The migration tests use randomly named temporary databases. They verify the exact fresh schema, stable template seeds, repeated execution, existing ADO.NET repository compatibility, rejection of an existing schema without migration history, and sanitized failure output.
