# QueueService

Automatically creates a daily queue entry when a patient checks in, so reception never has to add one manually. QueueService owns the `swiftcare_queue` database exclusively — no other service may query or write to it.

## What it does

- Consumes the `patient-checked-in` Kafka topic produced by PatientService for successful new-patient registration and returning-patient check-in.
- For each event, allocates the next sequential queue number for that clinic-local day (`Q-001`, `Q-002`, ...) inside a database transaction, and creates a `QueueEntry` with `Status = Waiting` and `RoomNumber = NULL`.
- The daily sequence resets at midnight in the clinic's local timezone (`Asia/Colombo` by default), not at UTC midnight — a UTC-date reset would roll the counter over at 05:30 local time instead.
- Idempotent against Kafka's at-least-once delivery: a redelivered event (same `EventId`) is recognized via a `ProcessedEvents` ledger and skipped without creating a duplicate entry.
- `UNIQUE (PatientId, QueueDate)` and `UNIQUE (QueueDate, QueueNumber)` constraints are the database-level backstop — a second event for the same patient on the same day (even with a different `EventId`) is rejected and logged, not silently duplicated.
- `GET /api/queue/today/patient/{patientId}` — returns whether a patient is in today's queue and their assigned queue number. Receptionist only.
- `GET /health` — liveness/readiness check.
- Enforces the Gateway trust boundary via `GatewaySecretMiddleware`, matching every other service.

The read API is deliberately limited to one patient's status for the reception profile workflow. It does not expose the full queue, waiting pool, room assignments, or public display data.

## Port

`5003` (see `Properties/launchSettings.json`).

## Dependencies

- .NET 10 SDK
- MySQL 8.4 reachable at the connection string in `ConnectionStrings:QueueDb` (see the repository root `docker-compose.yml` for local MySQL)
- EF Core 9 / Pomelo MySQL provider (pinned below EF Core 10 until Pomelo releases `net10` support)
- A reachable Kafka broker with the `patient-checked-in` topic (see the repository root `docker-compose.yml`) — reachability is not required at startup; a lost connection degrades the consumer (it retries via `Seek`), it does not block `/health`
- `dotnet-ef` as a local tool (already restored via the repository's `dotnet-tools.json`)

## Required environment variables

QueueService fails fast at startup if any of these are missing — it will not silently start in a misconfigured state.

| Variable | Purpose | Notes |
| --- | --- | --- |
| `ConnectionStrings__QueueDb` | MySQL connection string for `swiftcare_queue` | Required |
| `Gateway__InternalSecret` | Shared secret validated on every non-health request | Required, must match the API Gateway's `Gateway__InternalSecret` |
| `Kafka__BootstrapServers` | Address of the Kafka broker | Required to be *configured*; the broker itself does not need to be *reachable* for the service to start |

`Kafka:PatientCheckedInTopic` (`patient-checked-in`), `Kafka:ConsumerGroupId` (`queue-service`), `Kafka:RetryDelay` (5 seconds), `Queue:ClinicTimeZone` (`Asia/Colombo`), and `Queue:MaxAllocationAttempts` (3) are non-secret and already set in `appsettings.json`.

Never hardcode these values in source or commit them to `.env`. Set them via your shell, a local `.env` (never committed), or the orchestrator's secret store.

## Running locally

QueueService is part of the repository Compose stack. Apply its migrations before starting the worker against a new database volume:

```bash
# from the repository root after creating .env from .env.example
docker compose up --detach --wait mysql kafka
docker compose run --rm kafka-init
docker compose run --rm --no-deps queueservice --migrate
docker compose up --detach --no-deps --wait queueservice
curl http://localhost:5003/health
```

Running `docker compose up --detach` starts QueueService with the rest of the application after the database has been prepared. QueueService consumes Kafka messages in the background and serves the receptionist-only patient-status lookup through ApiGateway.

For controlled deployments, the published service image can apply migrations and
exit without starting the web host:

```bash
dotnet QueueService.dll --migrate
```

The command reads `ConnectionStrings__QueueDb`, retries transient MySQL failures,
returns a non-zero exit code on failure, and is safe to run repeatedly.

## API documentation

With `ASPNETCORE_ENVIRONMENT=Development`, an interactive API explorer (Scalar) is served at [`http://localhost:5003/scalar/v1`](http://localhost:5003/scalar/v1), reading the raw OpenAPI document at `/openapi/v1.json`. Both are reachable without an `X-Gateway-Secret` header — `GatewaySecretMiddleware` exempts them the same way it exempts `/health` — but they do not exist at all outside Development.

## Kafka

QueueService validates only that `Kafka:BootstrapServers` is *configured* at startup, never that the broker is *reachable* — the service must start and serve `/health` even when Kafka is down, per SwiftCare's independent-deployability rule. The consumer uses manual offset commits (`EnableAutoCommit = false`): an offset is only committed after the database transaction for that event has committed, so a crash between processing and committing causes Kafka to redeliver the message rather than lose it — safe because of the `ProcessedEvents` idempotency ledger. A malformed or undeserializable message is logged and its offset committed past (skipped), since no amount of redelivery would ever let it succeed. A database failure while processing a well-formed message is logged, the consumer's local position is rewound via `Seek` so the same message is redelivered, and it waits `Kafka:RetryDelay` before trying again — this means a genuinely broken message never blocks the partition, but a real database outage retries the same message indefinitely until the database recovers.

## Testing

```bash
dotnet test tests/QueueService.UnitTests/QueueService.UnitTests.csproj
```

CI runs this suite, applies the committed migrations to a clean MySQL database, checks for pending model changes, and builds the production Docker image.

Tests use `Microsoft.EntityFrameworkCore.Sqlite` (in-memory, relational) rather than the `InMemory` provider PatientService's tests use — `InMemory` enforces neither unique indexes nor transactions, so it cannot verify the `UNIQUE (PatientId, QueueDate)` / `UNIQUE (QueueDate, QueueNumber)` constraints or the transactional counter allocation this story's Definition of Done requires. Coverage includes: sequential queue-number assignment across multiple patients on the same day, `Status = Waiting` / `RoomNumber = NULL` on creation, the daily reset (a new clinic day restarts at `Q-001`, the prior day's entries are untouched), the `Asia/Colombo` midnight-boundary conversion (a UTC timestamp late in the evening correctly lands on the next local day), duplicate-`EventId` idempotency, same-patient-same-day rejection with a distinct `EventId`, confirmation that a rejected duplicate does not consume a queue number, `Q-010`/`Q-100` number-padding boundaries, both unique indexes verified directly against `QueueDbContext` independent of the service logic, and the Kafka consumer's message handling (valid dispatch and commit, malformed-payload skip, empty-`EventId` skip, and a processing failure causing `Seek` without `Commit`).

## Endpoints

| Method | Path | Auth | Description |
| --- | --- | --- | --- |
| `GET` | `/api/queue/today/patient/{patientId}` | `X-Gateway-Secret`, `X-User-Role: Receptionist` | Returns `{ isCheckedIn, queueNumber }` for today's clinic-local queue |
| `GET` | `/health` | none | Health check |

## Deployment

CD publishes `swiftcare-queue:<commit-sha>` to GHCR, runs `--migrate` through a finite Azure Container Apps Job, and deploys QueueService as a private background Container App without ingress. The worker keeps one replica active while the Azure environment is running so it can continuously consume `patient-checked-in` events. Credit-saving operations may explicitly stop the app; a later deployment starts it again.

Azure deployment uses a dedicated `swiftcare_queue` database account and requires the GitHub Environment values documented in the repository root README.

## Known scope bounds

- **No queue-list read API.** Only the per-patient status lookup exists. Full-queue, waiting-pool, room-assignment, and public-display APIs remain separate stories.
- **`ProcessedEvents` has no retention policy.** It grows unbounded — years of headroom at clinic check-in volume, but a deliberate gap if it ever needs cleanup.
- **Queue numbers are not gap-free.** A transaction that rolls back after incrementing the counter leaves a gap in that day's sequence. The story requires "the next daily queue number", not gapless numbering.
- **Unknown `PatientId` is trusted, not verified.** The consumer never calls back into PatientService to confirm a patient exists — it trusts the event, since it originates from the owning service and a synchronous callback would couple this service's availability to PatientService's.
