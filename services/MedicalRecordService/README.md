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

`MedicalRecordDbContext` and the committed files under `Migrations/` manage the `ConsultationTemplates` and `Consultations` tables. The initial migration seeds the general, respiratory, gastrointestinal, and musculoskeletal templates with stable GUIDs.

With Docker Compose, `medicalrecord-migrate` runs `--migrate` before MedicalRecordService starts. To run it separately with the service image:

```powershell
docker compose run --rm --no-deps medicalrecord-migrate
```

To run it directly from the repository, configure the connection and execute the maintenance command:

```powershell
$env:ConnectionStrings__MedicalRecordDb = "Server=localhost;Port=3306;Database=swiftcare_medical_record;User Id=<MYSQL_USER>;Password=<MYSQL_PASSWORD>;"
dotnet run --project services/MedicalRecordService -- --migrate
```

The same command runs through the existing Azure Container Apps job inside the VNet. The process exits with a non-zero code if it cannot connect, validate a legacy schema, or apply a migration. Running it again after all migrations have been applied succeeds without recreating tables or duplicating templates.

### Upgrading a database created by `schema.sql`

The migration command can safely adopt the previous SQL-created schema:

1. Back up `swiftcare_medical_record`, stop application writes, and record the consultation and template row counts.
2. Run the new service image once with `--migrate`. Do not manually insert an EF migration-history row.
3. The runner detects the two legacy tables and validates their columns, nullability, GUID representation, indexes, template foreign key, and all four stable template records.
4. Only after validation succeeds, it records `20260916082514_InitialMedicalRecordSchema` in `__EFMigrationsHistory`. Existing consultations, templates, and their timestamps are not modified.
5. Run `--migrate` a second time. It should report that no migrations are pending.
6. Compare the consultation and template row counts with the values recorded before the upgrade, then allow application writes again.

If the legacy schema is incomplete or incompatible, the command exits unsuccessfully before recording the baseline. Correct the schema mismatch or restore the backup, then rerun the command. Do not mark the migration as applied manually because that would bypass the compatibility checks.

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

The migration tests use randomly named temporary databases. They verify fresh creation, stable template seeds, repeated execution, existing ADO.NET repository compatibility, legacy-schema baselining without data changes, incomplete-schema rejection, and sanitized failure output.
