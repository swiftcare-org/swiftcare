# PatientService

Registers and stores patient records for SwiftCare. PatientService owns the `swiftcare_patient` database exclusively — no other service may query or write to it.

## What it does

- `POST /api/patients` — registers a new patient (NIC, full name, date of birth, gender, address, phone number, blood group), rejecting duplicate NICs. Publishes a `patient-checked-in` Kafka event on success.
- `GET /api/patients/search?q=` — searches patients by partial or full name, NIC, or phone number, case insensitive. Returns up to 20 matches (name, NIC, phone, blood group only), ordered by name. A term shorter than 2 characters, or no term at all, returns an empty array rather than a validation error. Open to Doctor, Receptionist, and Admin.
- `GET /api/patients/{id}` — returns a patient's profile for the patient-profile view. Open to Doctor, Receptionist, and Admin.
- `PUT /api/patients/{id}` — updates a patient's address, phone number, and blood group. NIC and date of birth are not part of the update contract. Receptionist only.
- `POST /api/patients/{id}/check-in` — verifies that a returning patient exists and publishes a `patient-checked-in` event with `isNewPatient: false`. Returns `202 Accepted` after publication; QueueService assigns the queue number asynchronously. Receptionist only.
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
- A reachable Kafka broker for the `patient-checked-in` topic (see the repository root `docker-compose.yml`) — reachability is not required at startup, only when registration or returning-patient check-in publishes an event (see "Kafka" below)
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

For controlled deployments, the published service image can apply migrations and
exit without starting the web host:

```bash
dotnet PatientService.dll --migrate
```

The command reads `ConnectionStrings__PatientDb`, retries transient MySQL failures,
returns a non-zero exit code on failure, and is safe to run repeatedly.

## API documentation

With `ASPNETCORE_ENVIRONMENT=Development`, an interactive API explorer (Scalar) is served at [`http://localhost:5002/scalar/v1`](http://localhost:5002/scalar/v1), reading the raw OpenAPI document at `/openapi/v1.json`. Both are reachable without an `X-Gateway-Secret` header — `GatewaySecretMiddleware` exempts them the same way it exempts `/health` — but they do not exist at all outside Development, since `MapOpenApi()`/`MapScalarApiReference()` are only registered inside that environment guard.

## Kafka

PatientService validates only that `Kafka:BootstrapServers` is *configured* at startup, never that the broker is *reachable* — the service must start and serve `/health` even when Kafka is down, per SwiftCare's independent-deployability rule. Every publish is bounded by `Kafka:MessageTimeoutMs` (default 5000ms) so an unreachable broker cannot hang an HTTP request. Registration still returns `201 Created` after a publish failure because the new patient record has already been committed as the source of truth. Returning-patient check-in instead returns `503 Service Unavailable` when publication fails, because no new patient data was created and the check-in cannot reach QueueService. Successful returning check-in returns `202 Accepted`; the frontend then reads QueueService until the asynchronously assigned queue number is available.

## Testing

```bash
dotnet test tests/PatientService.UnitTests/PatientService.UnitTests.csproj
```

Tests use EF Core InMemory and Moq exclusively — no real database, network connection, or Kafka broker is required to run them. Coverage includes DTO validation (NIC/phone formats, blood group, date-of-birth bounds, allergy name/severity/notes), the Kafka publisher (topic, key, payload shape, `isNewPatient` values, no-PHI assertion, timeout/failure handling), the registration and returning-patient check-in services, patient search, patient profile retrieval and update, allergy management, and the full controller pipeline via `WebApplicationFactory` including role authorization and error responses.

## Endpoints

| Method | Path | Auth | Description |
| --- | --- | --- | --- |
| `POST` | `/api/patients` | `X-Gateway-Secret`, `X-User-Role: Receptionist` | Registers a patient, returns `{ patientId, createdAt }` on success |
| `GET` | `/api/patients/search?q=` | `X-Gateway-Secret`, `X-User-Role: Doctor\|Receptionist\|Admin` | Searches by name/NIC/phone, returns an array of `{ patientId, fullName, nic, phoneNumber, bloodGroup }` (possibly empty) |
| `GET` | `/api/patients/{id}` | `X-Gateway-Secret`, `X-User-Role: Doctor\|Receptionist\|Admin` | Returns the patient's profile, or `404` if unknown |
| `PUT` | `/api/patients/{id}` | `X-Gateway-Secret`, `X-User-Role: Receptionist` | Updates address, phone number, and blood group, or returns `404` if unknown |
| `POST` | `/api/patients/{id}/check-in` | `X-Gateway-Secret`, `X-User-Role: Receptionist` | Publishes a returning-patient check-in and returns `202`, `404`, or `503` |
| `GET` | `/api/patients/{id}/allergies` | `X-Gateway-Secret`, `X-User-Role: Doctor\|Receptionist\|Admin` | Returns the patient's allergies (Severe first), or `404` if the patient is unknown |
| `POST` | `/api/patients/{id}/allergies` | `X-Gateway-Secret`, `X-User-Role: Doctor\|Receptionist` | Records an allergy, returns `201` with the created allergy |
| `PUT` | `/api/patients/{id}/allergies/{allergyId}` | `X-Gateway-Secret`, `X-User-Role: Doctor\|Receptionist` | Updates an allergy, returns `200` with the updated allergy, or `404` if the allergy doesn't belong to this patient |
| `DELETE` | `/api/patients/{id}/allergies/{allergyId}` | `X-Gateway-Secret`, `X-User-Role: Doctor\|Receptionist` | Soft-deletes an allergy, returns `204` |
| `GET` | `/health` | none | Health check |

See `Controllers/PatientsController.cs` and `Controllers/AllergiesController.cs` for the exact request/response contracts and status-code mapping.

## Known scope bounds

- PatientService never assigns or returns a queue number. QueueService owns numbering; after `202 Accepted`, the frontend obtains the assigned number through QueueService's patient-status endpoint.
- No phone-number normalization: a patient stored as `+94771234567` is not found by a search for `0771234567`, or vice versa, unless the shared digits are typed.
- Patient search is not audited — nothing records who searched for what. If searches must be audit-logged for compliance, that is a separate story.
- QueueService consumes `patient-checked-in` (see its own README); verify publication via Kafka UI (`localhost:8080` in the local compose stack) or the resulting `QueueEntries` row.
- **Allergies live in PatientService, not MedicalRecordService.** The README and PRODUCT.md assign allergies (and other clinical records) to MedicalRecordService, which does not exist yet (an empty placeholder directory, not in `SwiftCare.slnx`). SWC-17 was placed here by explicit stakeholder decision rather than waiting on that service. This is a deliberate database-boundary trade-off: when MedicalRecordService is eventually built, the `Allergies` table will need to migrate out of `swiftcare_patient` into `swiftcare_medical_record` via each service's API, not a direct database copy, per SwiftCare's cross-service data rule.
- No optimistic concurrency on allergy updates — two concurrent edits to the same allergy resolve last-write-wins, silently.
- Duplicate allergy names for the same patient are permitted; there is no uniqueness constraint.
