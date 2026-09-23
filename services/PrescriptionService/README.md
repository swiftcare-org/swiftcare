# PrescriptionService

PrescriptionService owns digital prescriptions and their medicine items. It stores references to the consultation, queue entry, patient, and prescribing doctor while keeping its data inside the service-owned `swiftcare_prescription` database.

## What it does

- Creates one prescription for a completed consultation.
- Stores the related `ConsultationId`, `QueueId`, and `PatientId` as opaque cross-service identifiers.
- Reads the doctor ID and full name from identity headers supplied by the authenticated API Gateway request.
- Sets every new prescription to `PENDING`.
- Stores multiple medicines in the order entered by the doctor.
- Returns a patient's previous prescriptions newest first for clinical reference.
- Rejects a second prescription for the same consultation.

Allergy details remain owned by PatientService. The frontend reads them from PatientService and displays advisory warnings on the prescription form; PrescriptionService neither copies allergies nor blocks a prescription because an allergy exists.

## Port

`5001` for local development.

## Endpoints

| Method | Path | Authorization | Description |
| --- | --- | --- | --- |
| `POST` | `/api/prescriptions` | Doctor | Creates a `PENDING` prescription with one or more medicines. |
| `GET` | `/api/prescriptions/patient/{patientId}` | Doctor | Returns the patient's prescriptions newest first, including ordered medicine items. |
| `GET` | `/health` | Anonymous | Service health check. |

The API Gateway exposes both prescription API routes with its `DoctorOnly` policy. PrescriptionService also verifies the forwarded `X-User-Role`, `X-User-Id`, and `X-User-Name` headers after `GatewaySecretMiddleware` validates `X-Gateway-Secret`.

## Create request

```json
{
  "consultationId": "11111111-1111-1111-1111-111111111111",
  "queueId": "22222222-2222-2222-2222-222222222222",
  "patientId": "33333333-3333-3333-3333-333333333333",
  "medicines": [
    {
      "medicineName": "Amoxicillin",
      "dosage": "500 mg",
      "frequency": "Twice daily",
      "duration": "5 days",
      "instructions": "After meals"
    }
  ]
}
```

Medicine name, dosage, frequency, and duration are required. Instructions are optional. An empty list returns a validation error containing `Add at least one medicine`. A repeated `ConsultationId` returns HTTP `409` with `A prescription already exists for this consultation`.

## Data model

`PrescriptionDbContext` owns:

- `Prescriptions` — linkage identifiers, trusted doctor identity, status, and UTC timestamps.
- `PrescriptionItems` — medicine name, dosage, frequency, duration, optional instructions, and item order.

The database enforces a unique index on `ConsultationId` and a unique `(PrescriptionId, ItemOrder)` index. Deleting a prescription cascades to its medicine items. `(PatientId, CreatedAt)` supports history reads without copying patient demographics.

## Configuration

| Variable | Purpose |
| --- | --- |
| `ConnectionStrings__PrescriptionDb` | MySQL connection for the service-owned `swiftcare_prescription` database. |
| `Gateway__InternalSecret` | Shared secret required on non-health requests forwarded by API Gateway. |
| `ASPNETCORE_ENVIRONMENT` | Set to `Development` to expose OpenAPI and Scalar locally. |

Secrets must come from local environment variables or an approved secret store. Do not put passwords or the Gateway secret in committed settings files.

## Run locally

Start MySQL, then apply the committed migration:

```powershell
dotnet tool restore
dotnet ef database update --project services/PrescriptionService --connection "Server=localhost;Port=3306;Database=swiftcare_prescription;User Id=<MYSQL_USER>;Password=<MYSQL_PASSWORD>;"
```

Start PrescriptionService separately because SWC-29 does not add it to Docker Compose:

```powershell
$env:ConnectionStrings__PrescriptionDb = "Server=localhost;Port=3306;Database=swiftcare_prescription;User Id=<MYSQL_USER>;Password=<MYSQL_PASSWORD>;"
$env:Gateway__InternalSecret = "<same value used by ApiGateway>"
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run --project services/PrescriptionService
```

The local API Gateway cluster forwards prescription requests to `http://localhost:5001`.

## Tests

```powershell
dotnet test tests/PrescriptionService.UnitTests/PrescriptionService.UnitTests.csproj
```

Backend coverage includes request validation, required linkage, trusted doctor identity, `PENDING` status, ordered medicine persistence, duplicate-consultation prevention, role handling, patient-history ordering, and empty history. Frontend behavior is validated with lint, a production build, and manual QA; the repository does not use frontend unit tests.

## Scope boundaries

- SWC-29 does not add Docker Compose, CI/CD image publishing, Terraform, Azure database resources, or Container App deployment for PrescriptionService.
- Prescription dispensing and the receptionist's enabled `View Prescription` workflow belong to SWC-30.
- PrescriptionService does not query PatientService, MedicalRecordService, or QueueService databases.
