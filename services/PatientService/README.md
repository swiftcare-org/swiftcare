# PatientService

Registers and stores patient records for SwiftCare. PatientService owns the `swiftcare_patient` database exclusively — no other service may query or write to it.

## What it does

- `POST /api/patients` — registers a new patient (NIC, full name, date of birth, gender, address, phone number, blood group), rejecting duplicate NICs. Publishes a `patient-checked-in` Kafka event on success.
- `GET /api/patients/search?q=` — searches patients by partial or full name, NIC, or phone number, case insensitive. Returns up to 20 matches (name, NIC, phone, blood group only), ordered by name. A term shorter than 2 characters, or no term at all, returns an empty array rather than a validation error. Open to Doctor, Receptionist, and Admin.
- `GET /api/patients/{id}` — returns a patient's profile for the patient-profile view. Open to Doctor, Receptionist, and Admin.
- `GET /api/patients/{id}/allergies` — returns a patient's recorded allergies, ordered Severe first, then Moderate, then Mild, newest first within a severity. Open to Doctor, Receptionist, and Admin.
- `POST /api/patients/{id}/allergies` — records an allergy (name, severity, optional notes). Doctor and Receptionist only.
- `PUT /api/patients/{id}/allergies/{allergyId}` — updates an allergy's name, severity, and notes. Doctor and Receptionist only.
- `DELETE /api/patients/{id}/allergies/{allergyId}` — soft-deletes an allergy (`IsDeleted = true`); the row is never hard-deleted, preserving the clinical audit trail. Doctor and Receptionist only.
- `GET /health` — liveness/readiness check.
- Enforces the Gateway trust boundary: every request except `/health` must carry a valid `X-Gateway-Secret` header.

## Port

`5002` (see `Properties/launchSettings.json`).

## Dependencies

- .NET 10 SDK
- MySQL 8.4 reachable at the connection string in `ConnectionStrings:PatientDb` (see the repository root `docker-compose.yml` for local MySQL)
- EF Core 9 / Pomelo MySQL provider (pinned below EF Core 10 until Pomelo releases `net10` support)
- A reachable Kafka broker for the `patient-checked-in` topic (see the repository root `docker-compose.yml`) — reachability is not required at startup, only for the publish step of registration (see "Kafka" below)
- `dotnet-ef` as a local tool (already restored via the repository's `dotnet-tools.json`)

## Required environment variables

PatientService fails fast at startup if any of these are missing — it will not silently start in a misconfigured state.

| Variable | Purpose | Notes |
| --- | --- | --- |
| `ConnectionStrings__PatientDb` | MySQL connection string for `swiftcare_patient` | Required |
| `Gateway__InternalSecret` | Shared secret validated on every non-health request | Required, must match the API Gateway's `Gateway__InternalSecret` |
| `Kafka__BootstrapServers` | Address of the Kafka broker | Required to be *configured*; the broker itself does not need to be *reachable* for the service to start (see "Kafka" below) |

`Kafka:PatientCheckedInTopic` (`patient-checked-in`) and `Kafka:MessageTimeoutMs` (`5000`) are non-secret and already set in `appsettings.json`.

Never hardcode these values in source or commit them to `.env`. Set them via your shell, a local `.env` (never committed), or the orchestrator's secret store.

## Running locally

```bash
# from the repository root, with MySQL and Kafka up (docker compose up -d)
cd services/PatientService

export ConnectionStrings__PatientDb="Server=localhost;Port=3306;Database=swiftcare_patient;User=<user>;Password=<password>;"
export Gateway__InternalSecret="<shared value, must match ApiGateway>"
export Kafka__BootstrapServers="localhost:9092"
export ASPNETCORE_ENVIRONMENT=Development

dotnet ef database update
dotnet run
```

## API documentation

With `ASPNETCORE_ENVIRONMENT=Development`, an interactive API explorer (Scalar) is served at [`http://localhost:5002/scalar/v1`](http://localhost:5002/scalar/v1), reading the raw OpenAPI document at `/openapi/v1.json`. Both are reachable without an `X-Gateway-Secret` header — `GatewaySecretMiddleware` exempts them the same way it exempts `/health` — but they do not exist at all outside Development, since `MapOpenApi()`/`MapScalarApiReference()` are only registered inside that environment guard.

## Kafka

PatientService validates only that `Kafka:BootstrapServers` is *configured* at startup, never that the broker is *reachable* — the service must start and serve `/health` even when Kafka is down, per SwiftCare's independent-deployability rule. If the broker is unreachable when a patient is registered, the publish is bounded by `Kafka:MessageTimeoutMs` (default 5000ms) so the request cannot hang, the failure is logged at `Error` with the patient ID and correlation ID, and the registration request still returns `201 Created` — the patient record is the source of truth and is not rolled back. A transactional outbox for guaranteed event delivery is a future story; today, a lost event means the patient exists but was never queued, with no automatic reconciliation.

## Testing

```bash
dotnet test tests/PatientService.UnitTests/PatientService.UnitTests.csproj
```

Tests use EF Core InMemory and Moq exclusively — no real database, network connection, or Kafka broker is required to run them. Coverage includes DTO validation (NIC/phone formats, blood group, date-of-birth bounds, allergy name/severity/notes), the Kafka publisher (topic, key, payload shape, no-PHI assertion, timeout/failure handling), the registration service (duplicate NIC including soft-deleted rows, NIC normalization, publish-failure tolerance), patient search (partial/case-insensitive matching across name/NIC/phone, soft-delete exclusion, result cap and ordering, empty-result and below-minimum-length handling, no-unnecessary-PHI response shape), the patient profile service (existing/unknown/soft-deleted patient), the allergy service (severity ordering, soft-delete exclusion, cross-patient isolation on every operation, blank-notes normalization), and the full controller pipeline via `WebApplicationFactory` (200/201/204/400/401/403/404 paths) including per-role authorization on every allergy endpoint.

## Endpoints

| Method | Path | Auth | Description |
| --- | --- | --- | --- |
| `POST` | `/api/patients` | `X-Gateway-Secret`, `X-User-Role: Receptionist` | Registers a patient, returns `{ patientId, createdAt }` on success |
| `GET` | `/api/patients/search?q=` | `X-Gateway-Secret`, `X-User-Role: Doctor\|Receptionist\|Admin` | Searches by name/NIC/phone, returns an array of `{ patientId, fullName, nic, phoneNumber, bloodGroup }` (possibly empty) |
| `GET` | `/api/patients/{id}` | `X-Gateway-Secret`, `X-User-Role: Doctor\|Receptionist\|Admin` | Returns the patient's profile, or `404` if unknown |
| `GET` | `/api/patients/{id}/allergies` | `X-Gateway-Secret`, `X-User-Role: Doctor\|Receptionist\|Admin` | Returns the patient's allergies (Severe first), or `404` if the patient is unknown |
| `POST` | `/api/patients/{id}/allergies` | `X-Gateway-Secret`, `X-User-Role: Doctor\|Receptionist` | Records an allergy, returns `201` with the created allergy |
| `PUT` | `/api/patients/{id}/allergies/{allergyId}` | `X-Gateway-Secret`, `X-User-Role: Doctor\|Receptionist` | Updates an allergy, returns `200` with the updated allergy, or `404` if the allergy doesn't belong to this patient |
| `DELETE` | `/api/patients/{id}/allergies/{allergyId}` | `X-Gateway-Secret`, `X-User-Role: Doctor\|Receptionist` | Soft-deletes an allergy, returns `204` |
| `GET` | `/health` | none | Health check |

See `Controllers/PatientsController.cs` and `Controllers/AllergiesController.cs` for the exact request/response contracts and status-code mapping.

## Known scope bounds

- No queue number is returned or assigned. QueueService owns queue numbering and does not exist yet.
- No phone-number normalization: a patient stored as `+94771234567` is not found by a search for `0771234567`, or vice versa, unless the shared digits are typed.
- Patient search is not audited — nothing records who searched for what. If searches must be audit-logged for compliance, that is a separate story.
- Nothing consumes `patient-checked-in` yet; verify publication via Kafka UI (`localhost:8080` in the local compose stack).
- **Allergies live in PatientService, not MedicalRecordService.** The README and PRODUCT.md assign allergies (and other clinical records) to MedicalRecordService, which does not exist yet (an empty placeholder directory, not in `SwiftCare.slnx`). SWC-17 was placed here by explicit stakeholder decision rather than waiting on that service. This is a deliberate database-boundary trade-off: when MedicalRecordService is eventually built, the `Allergies` table will need to migrate out of `swiftcare_patient` into `swiftcare_medical_record` via each service's API, not a direct database copy, per SwiftCare's cross-service data rule.
- No optimistic concurrency on allergy updates — two concurrent edits to the same allergy resolve last-write-wins, silently.
- Duplicate allergy names for the same patient are permitted; there is no uniqueness constraint.
