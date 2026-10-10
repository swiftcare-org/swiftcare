# MedicalRecordService

MedicalRecordService owns consultation records, consultation templates, vital signs, and consultation follow-up details. It uses parameterized ADO.NET commands through `MySqlConnector` and accepts requests only after the API Gateway establishes its internal trust boundary.

## Port

`5004` (see `Properties/launchSettings.json`).

## Endpoints

| Method | Path | Auth | Description |
| --- | --- | --- | --- |
| `GET` | `/health` | none | Health check |
| `GET` | `/api/templates` | Doctor | Returns the built-in templates plus the requesting doctor's own, each marked with `isBuiltIn` |
| `POST` | `/api/templates` | Doctor | Saves a template for the requesting doctor. `201`, `400` for a missing or too-long value, `409` for a name the doctor already uses |
| `DELETE` | `/api/templates/{id}` | Doctor (owner) | Marks the doctor's own template inactive. `204`, `403` for a built-in template, `404` for a missing or another doctor's template |
| `POST` | `/api/consultations` | Doctor | Creates a consultation for the doctor's current queue assignment |
| `POST` | `/api/consultations/{consultationId}/vitals` | Doctor | Records vital signs for the doctor's consultation and calculates BMI |
| `GET` | `/api/consultations/by-queue/{queueId}` | Doctor | Returns this doctor's saved consultation status and whether vital signs exist, or `204` when none exists |
| `GET` | `/api/consultations/latest-completed` | Doctor | Returns this doctor's latest completed consultation identifiers so unfinished prescription entry can be recovered, or `204` when none exists |
| `GET` | `/api/consultations/patient/{patientId}/latest-follow-up` | Doctor | Returns the overdue follow-up from the patient's latest completed consultation, or `204` when none is overdue |
| `GET` | `/api/consultations/patient/{patientId}` | Doctor | Returns the patient's completed consultations from every doctor, newest first, or an empty list on a first visit |
| `GET` | `/api/consultations/patient/{patientId}/latest` | Doctor | Returns the patient's most recent completed consultation, or `204` when none exists |
| `GET` | `/api/vitals/patient/{patientId}` | Doctor | Returns the vital signs recorded in the patient's completed consultations, newest first, or an empty list when none exist |
| `POST` | `/api/consultations/{consultationId}/complete` | Doctor | Completes the consultation and publishes `consultation-completed` |

The Gateway supplies the authenticated doctor's ID, name, and room number. These values are not accepted from the request body. Symptoms and diagnosis are required; examination findings, notes, template selection, and follow-up details are optional. A follow-up date and instructions must be provided together, and instructions are limited to 500 characters.

Vital signs may include blood pressure, temperature, pulse rate, respiratory rate, oxygen saturation, height, and weight. Blood-pressure values must be supplied together, and every supplied measurement must be positive. BMI is calculated by the service when both height and weight are present and is rounded to two decimal places. A doctor can record one set of vital signs for a consultation assigned to that doctor.

## Follow-up alerts

The doctor-only follow-up endpoint reads the patient's latest completed consultation. It returns the stored follow-up only when its date is earlier than the current clinic date. The `Clinic:TimeZone` setting defaults to `Asia/Colombo` in `appsettings.json`. A follow-up due today or later is not overdue. Missing follow-up details and patients without a completed consultation return `204 No Content`.

An overdue follow-up is returned as:

```json
{
  "consultationId": "6f1c2a9b-8c11-4d8f-9b4a-3b0f4d8f6f0e",
  "followUpDate": "2026-09-11",
  "instructions": "Review blood pressure in 6 weeks",
  "doctorName": "Dr. Silva",
  "daysOverdue": 21
}
```

`doctorName` is the doctor who recorded the follow-up. `daysOverdue` counts whole clinic days since the follow-up date, so a follow-up due yesterday is `1`.

The frontend combines this result with allergies and chronic conditions on the patient profile. It displays individual red allergy banners first, amber condition banners second, and the blue overdue follow-up banner last.

## Patient history

The three patient history endpoints are read-only and doctor-only. They are not limited to the requesting doctor, so a doctor sees consultations recorded by colleagues and can follow the patient's care across visits. Only `COMPLETE` consultations are returned, which leaves out the consultation that is still in progress. Patient IDs are GUIDs, matching the other patient-scoped routes, and `Consultations.PatientId` is the only key used to find a patient's records, since vital signs are joined through their consultation.

Timestamps are returned as UTC. Measurements that were not recorded are `null`, and the frontend compares each reading with the nearest older reading that recorded the same measurement to show up, down or unchanged trends.

## Completing a consultation

The doctor must save vital signs before completing a consultation. MedicalRecordService checks the trusted doctor identity and returns `409` with `Please save vital signs first` if they are missing. On the first completion request, it commits `Status = COMPLETE` and a new `EventId` in the `Consultations` table, then publishes `consultation-completed` to Kafka with that stored ID. The event contains identifiers and the diagnosis, which the department reports count. It never contains symptoms, clinical notes or patient details.

If publishing fails, the endpoint returns `503` with `Consultation could not be completed. Please try again.` The database still contains `COMPLETE` and the stored `EventId`, while QueueService leaves the queue entry `IN_CONSULTATION` until it receives the event. The doctor can retry Complete; the service skips the database write and republishes with the same `EventId`. The doctor-scoped `by-queue` endpoint lets the consultation page recover the saved consultation and vital-sign state after a refresh so this retry remains available. A successful publish does not by itself prove that QueueService has processed the event yet.

## EF Core migrations

`MedicalRecordDbContext` and the committed files under `Migrations/` manage the `ConsultationTemplates`, `Consultations`, and `VitalSigns` tables, including consultation follow-up details, completion status, and `EventId`. The initial migration seeds the general, respiratory, gastrointestinal, and musculoskeletal templates with stable GUIDs. The normal MedicalRecordService application image supports both API and migration execution; no separate migration image is required.

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

Each queue entry can have at most one consultation record, and each consultation can have at most one vital-sign record. The chosen template ID and name are stored on the consultation so the template used for the visit remains identifiable.

### Doctor-owned templates

A template with no `CreatedByDoctorId` is built in: every doctor sees it and nobody can remove it. A template with an owner is private to that doctor. The owner always comes from the Gateway identity header, never from the request body, and a consultation can only record a built-in template or one the same doctor owns.

A template needs a name (up to 100 characters) and symptoms, examination findings and notes (up to 5000 characters each). Names are unique per owner among active templates, enforced by the `UX_ConsultationTemplates_ActiveOwnerScope_Name` index over a generated column, so two doctors can use the same name and a removed template frees its name for reuse.

Removing a template sets `IsActive` to false and keeps the row. Past consultations hold their own copy of the clinical text and the template name, so they load unchanged.

## Required environment variables

| Variable | Purpose |
| --- | --- |
| `ConnectionStrings__MedicalRecordDb` | MySQL connection string for `swiftcare_medical_record` |
| `Gateway__InternalSecret` | Shared secret that must match the API Gateway |
| `Kafka__BootstrapServers` | Kafka broker address used to publish `consultation-completed` |
| `ASPNETCORE_ENVIRONMENT` | Use `Development` locally to expose OpenAPI and Scalar |

Example local configuration:

```powershell
$env:ConnectionStrings__MedicalRecordDb = "Server=localhost;Port=3306;Database=$env:MEDICAL_RECORD_DB_NAME;User Id=$env:MYSQL_USER;Password=$env:MYSQL_PASSWORD;"
$env:Gateway__InternalSecret = $env:GATEWAY_INTERNAL_SECRET
$env:Kafka__BootstrapServers = "localhost:9092"
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

The unit tests cover consultation creation and identity linkage, template prefill data, required-field validation, duplicate queue protection, vital-sign persistence, BMI calculation, completion ordering and retry, overdue follow-up date logic, role enforcement, Gateway-secret enforcement, and maintenance-command parsing.

### Completion event time

Completion stores a UTC CompletedAt value together with COMPLETE and EventId. Retries read the original value rather than the retry time. Apply the AddConsultationCompletedAt migration before deploying the producer. Existing completed rows remain null because their actual completion time cannot be reconstructed.

QueueService and NotificationService accept the optional CompletedAt field. Legacy events without it retain receipt-time behavior. Reports use event time when present; ReceivedAt remains the ingestion time. Deploy consumers before the producer. Existing notifications are not backfilled.

The MySQL completion regression test runs when SWIFTCARE_TEST_MYSQL points to an isolated MySQL 8.4 server. It creates and removes its own uniquely named test database.
