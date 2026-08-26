# PatientService

Registers and stores patient records for SwiftCare. PatientService owns the `swiftcare_patient` database exclusively — no other service may query or write to it.

## What it does

- `POST /api/patients` — registers a new patient (NIC, full name, date of birth, gender, address, phone number, blood group), rejecting duplicate NICs. Publishes a `patient-checked-in` Kafka event on success.
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

## Kafka

PatientService validates only that `Kafka:BootstrapServers` is *configured* at startup, never that the broker is *reachable* — the service must start and serve `/health` even when Kafka is down, per SwiftCare's independent-deployability rule. If the broker is unreachable when a patient is registered, the publish is bounded by `Kafka:MessageTimeoutMs` (default 5000ms) so the request cannot hang, the failure is logged at `Error` with the patient ID and correlation ID, and the registration request still returns `201 Created` — the patient record is the source of truth and is not rolled back. A transactional outbox for guaranteed event delivery is a future story; today, a lost event means the patient exists but was never queued, with no automatic reconciliation.

## Testing

```bash
dotnet test tests/PatientService.UnitTests/PatientService.UnitTests.csproj
```

Tests use EF Core InMemory and Moq exclusively — no real database, network connection, or Kafka broker is required to run them. Coverage includes DTO validation (NIC/phone formats, blood group, date-of-birth bounds), the Kafka publisher (topic, key, payload shape, no-PHI assertion, timeout/failure handling), the registration service (duplicate NIC including soft-deleted rows, NIC normalization, publish-failure tolerance), and the full controller pipeline via `WebApplicationFactory` (201/400/401/403 paths).

## Endpoints

| Method | Path | Auth | Description |
| --- | --- | --- | --- |
| `POST` | `/api/patients` | `X-Gateway-Secret`, `X-User-Role: Receptionist` | Registers a patient, returns `{ patientId, createdAt }` on success |
| `GET` | `/health` | none | Health check |

See `Controllers/PatientsController.cs` for the exact request/response contract and status-code mapping.

## Known scope bounds

- No queue number is returned or assigned. QueueService owns queue numbering and does not exist yet.
- No `GET /api/patients`, patient search, update, or delete endpoints.
- The duplicate-NIC error does not include a search link — patient search does not exist yet.
- Nothing consumes `patient-checked-in` yet; verify publication via Kafka UI (`localhost:8080` in the local compose stack).
