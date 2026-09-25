# PrescriptionService

PrescriptionService owns digital prescriptions and their medicine items. It stores references to the consultation, queue entry, patient, and prescribing doctor while keeping its data inside the service-owned `swiftcare_prescription` database.

## What it does

- Creates one prescription for a completed consultation.
- Stores the related `ConsultationId`, `QueueId`, and `PatientId` as opaque cross-service identifiers.
- Reads the doctor ID and full name from identity headers supplied by the authenticated API Gateway request.
- Sets every new prescription to `PENDING`.
- Stores multiple medicines in the order entered by the doctor.
- Allows the prescribing doctor to add or remove medicines from a saved prescription.
- Keeps at least one medicine and rejects changes after the prescription is `DISPENSED`.
- Returns a patient's previous prescriptions newest first for clinical reference.
- Rejects a second prescription for the same consultation.
- Retrieves the prescription linked to a queue entry for the shared staff details page.
- Allows a receptionist to dispense a `PENDING` prescription once and records their trusted name and the UTC dispensing time.

The doctor dashboard and prescription page can recover an unfinished prescription after navigation state is lost. They read the authenticated doctor's latest completed consultation from MedicalRecordService and compare its consultation ID with PrescriptionService history before offering the form again.

Allergy details remain owned by PatientService. The frontend reads them from PatientService and displays advisory warnings on the prescription form; PrescriptionService neither copies allergies nor blocks a prescription because an allergy exists.

## Port

`5001` for local development.

## Endpoints

| Method | Path | Authorization | Description |
| --- | --- | --- | --- |
| `POST` | `/api/prescriptions` | Doctor | Creates a `PENDING` prescription with one or more medicines. |
| `POST` | `/api/prescriptions/{prescriptionId}/items` | Doctor | Adds a medicine to the doctor's saved prescription. |
| `DELETE` | `/api/prescriptions/{prescriptionId}/items/{medicineId}` | Doctor | Removes a medicine when at least one other medicine remains. |
| `GET` | `/api/prescriptions/patient/{patientId}` | Doctor | Returns the patient's prescriptions newest first, including ordered medicine items. |
| `GET` | `/api/prescriptions/queue/{queueId}` | Doctor, Receptionist, Admin | Returns the prescription and ordered medicines for a queue entry. |
| `PUT` | `/api/prescriptions/{prescriptionId}/dispense` | Receptionist | Changes a `PENDING` prescription to `DISPENSED` and records who dispensed it and when. |
| `GET` | `/health` | Anonymous | Service health check. |

The API Gateway uses `DoctorOnly` for prescription creation, history, and medicine changes; `PrescriptionReadPolicy` for queue-based details; and `ReceptionistOnly` for dispensing. PrescriptionService repeats the relevant role and identity checks after `GatewaySecretMiddleware` validates `X-Gateway-Secret` and the Gateway supplies trusted identity headers.

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

## Change saved medicines

The prescribing doctor can add another medicine to a saved prescription by sending one medicine object to `POST /api/prescriptions/{prescriptionId}/items`. New medicines are appended after the existing items. The response contains the updated prescription and its ordered medicine list.

Removing a medicine uses `DELETE /api/prescriptions/{prescriptionId}/items/{medicineId}`. The frontend asks for confirmation before calling this endpoint. Removing the final medicine returns HTTP `409` with `Prescription must have at least one medicine`.

Both operations are limited to the doctor who created the prescription. A prescription with `DISPENSED` status is read-only, and either operation returns HTTP `409` with `Cannot modify a dispensed prescription`.

## Dispense a prescription

Doctors, receptionists, and administrators can view prescription details through `GET /api/prescriptions/queue/{queueId}`. Only a receptionist can use `PUT /api/prescriptions/{prescriptionId}/dispense`.

Dispensing changes the status from `PENDING` to `DISPENSED`, stores the receptionist's trusted `X-User-Name` value in `DispensedBy`, and stores the current UTC time in `DispensedAt`. A second dispense attempt returns HTTP `409` with `Prescription has already been dispensed` and preserves the original receptionist and timestamp.

## Data model

`PrescriptionDbContext` owns:

- `Prescriptions` — linkage identifiers, trusted doctor identity, status, dispensing details, and UTC timestamps.
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

Backend coverage includes request validation, required linkage, trusted doctor identity, `PENDING` status, ordered medicine persistence, duplicate-consultation prevention, role handling, patient-history and queue lookup, medicine additions and removals, minimum-one enforcement, prescription ownership, dispensing identity and timestamps, duplicate-dispense prevention, dispensed read-only behavior, and empty history. Frontend behavior is validated with lint, a production build, and manual QA; the repository does not use frontend unit tests.

## Scope boundaries

- SWC-29 does not add Docker Compose, CI/CD image publishing, Terraform, Azure database resources, or Container App deployment for PrescriptionService.
- SWC-41 adds the dispensing application workflow but does not add PrescriptionService to Docker Compose or Azure deployment.
- PrescriptionService does not query PatientService, MedicalRecordService, or QueueService databases.
