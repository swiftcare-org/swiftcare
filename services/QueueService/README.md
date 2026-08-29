# QueueService

Automatically creates a daily queue entry when a patient checks in, so reception never has to add one manually. QueueService owns the `swiftcare_queue` database exclusively — no other service may query or write to it.

## What it does

- Consumes the `patient-checked-in` Kafka topic (produced by PatientService on successful patient registration).
- For each event, allocates the next sequential queue number for that clinic-local day (`Q-001`, `Q-002`, ...) inside a database transaction, and creates a `QueueEntry` with `Status = Waiting` and `RoomNumber = NULL`.
- The daily sequence resets at midnight in the clinic's local timezone (`Asia/Colombo` by default), not at UTC midnight — a UTC-date reset would roll the counter over at 05:30 local time instead.
- Idempotent against Kafka's at-least-once delivery: a redelivered event (same `EventId`) is recognized via a `ProcessedEvents` ledger and skipped without creating a duplicate entry.
- `UNIQUE (PatientId, QueueDate)` and `UNIQUE (QueueDate, QueueNumber)` constraints are the database-level backstop — a second event for the same patient on the same day (even with a different `EventId`) is rejected and logged, not silently duplicated.
- `GET /health` — liveness/readiness check.
- Enforces the Gateway trust boundary via `GatewaySecretMiddleware`, matching every other service, even though this service currently exposes no other HTTP endpoint.

This story (SWC-19) is deliberately consume-only. There is no read API yet — no endpoint returns queue entries, and no frontend surface exists. Displaying the queue (queue number, waiting list, calling patients) is a separate future story.

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

QueueService is not yet wired into the repository root `docker-compose.yml` (a pending DevOps handoff — see "Known scope bounds" below), so run it standalone alongside the rest of the compose stack:

```bash
# from the repository root, with MySQL and Kafka up (docker compose up -d)
cd services/QueueService

export ConnectionStrings__QueueDb="Server=localhost;Port=3306;Database=swiftcare_queue;User=<user>;Password=<password>;"
export Gateway__InternalSecret="<shared value, must match ApiGateway>"
export Kafka__BootstrapServers="localhost:9092"
export ASPNETCORE_ENVIRONMENT=Development

dotnet ef database update
dotnet run
```

For controlled deployments, the published service image can apply migrations and
exit without starting the web host:

```bash
dotnet QueueService.dll --migrate
```

The command reads `ConnectionStrings__QueueDb`, retries transient MySQL failures,
returns a non-zero exit code on failure, and is safe to run repeatedly.

## API documentation

With `ASPNETCORE_ENVIRONMENT=Development`, an interactive API explorer (Scalar) is served at [`http://localhost:5003/scalar/v1`](http://localhost:5003/scalar/v1), reading the raw OpenAPI document at `/openapi/v1.json`. Both are reachable without an `X-Gateway-Secret` header — `GatewaySecretMiddleware` exempts them the same way it exempts `/health` — but they do not exist at all outside Development. There is currently nothing to document beyond `/health`, since this service exposes no business endpoints yet.

## Kafka

QueueService validates only that `Kafka:BootstrapServers` is *configured* at startup, never that the broker is *reachable* — the service must start and serve `/health` even when Kafka is down, per SwiftCare's independent-deployability rule. The consumer uses manual offset commits (`EnableAutoCommit = false`): an offset is only committed after the database transaction for that event has committed, so a crash between processing and committing causes Kafka to redeliver the message rather than lose it — safe because of the `ProcessedEvents` idempotency ledger. A malformed or undeserializable message is logged and its offset committed past (skipped), since no amount of redelivery would ever let it succeed. A database failure while processing a well-formed message is logged, the consumer's local position is rewound via `Seek` so the same message is redelivered, and it waits `Kafka:RetryDelay` before trying again — this means a genuinely broken message never blocks the partition, but a real database outage retries the same message indefinitely until the database recovers.

## Testing

```bash
dotnet test tests/QueueService.UnitTests/QueueService.UnitTests.csproj
```

Tests use `Microsoft.EntityFrameworkCore.Sqlite` (in-memory, relational) rather than the `InMemory` provider PatientService's tests use — `InMemory` enforces neither unique indexes nor transactions, so it cannot verify the `UNIQUE (PatientId, QueueDate)` / `UNIQUE (QueueDate, QueueNumber)` constraints or the transactional counter allocation this story's Definition of Done requires. Coverage includes: sequential queue-number assignment across multiple patients on the same day, `Status = Waiting` / `RoomNumber = NULL` on creation, the daily reset (a new clinic day restarts at `Q-001`, the prior day's entries are untouched), the `Asia/Colombo` midnight-boundary conversion (a UTC timestamp late in the evening correctly lands on the next local day), duplicate-`EventId` idempotency, same-patient-same-day rejection with a distinct `EventId`, confirmation that a rejected duplicate does not consume a queue number, `Q-010`/`Q-100` number-padding boundaries, both unique indexes verified directly against `QueueDbContext` independent of the service logic, and the Kafka consumer's message handling (valid dispatch and commit, malformed-payload skip, empty-`EventId` skip, and a processing failure causing `Seek` without `Commit`).

## Endpoints

| Method | Path | Auth | Description |
| --- | --- | --- | --- |
| `GET` | `/health` | none | Health check |

No other HTTP endpoints exist yet. See "What it does" above for the Kafka consumer's behavior instead.

## Known scope bounds

- **No read API.** Nothing returns queue entries over HTTP. Displaying the queue (queue number, waiting list, calling patients, room assignment) is a separate future story.
- **Not in `docker-compose.yml` or `.env.example`.** Run it standalone via `dotnet run` per "Running locally" above until DevOps adds a `queueservice` block and a `QUEUE_SERVICE_PORT` variable.
- **No `Dockerfile`.** Out of developer scope for this story; DevOps owns it alongside the compose entry.
- **Not in CI's `dotnet test` or `validate-migrations` jobs.** `tests/QueueService.UnitTests` exists and passes locally but does not yet gate pull requests, and the `InitialQueueSchema` migration is not yet applied to a clean database in CI, until `.github/workflows/ci.yml` is updated (DevOps-owned; developer scope for this story explicitly excludes CI/CD workflow changes).
- **Not in the CD pipeline.** No image build, no Container Apps deployment, no migration job.
- **No returning-patient check-in flow exists yet.** The only current producer of `patient-checked-in` is PatientService's *new*-patient registration endpoint (see PatientService's README). A returning patient checking in again is a separate story (SWC-13); until then, Scenario 4 (same patient, same day) is only reachable by hand-publishing a second event to Kafka, not through the running application.
- **`ProcessedEvents` has no retention policy.** It grows unbounded — years of headroom at clinic check-in volume, but a deliberate gap if it ever needs cleanup.
- **Queue numbers are not gap-free.** A transaction that rolls back after incrementing the counter leaves a gap in that day's sequence. The story requires "the next daily queue number", not gapless numbering.
- **Unknown `PatientId` is trusted, not verified.** The consumer never calls back into PatientService to confirm a patient exists — it trusts the event, since it originates from the owning service and a synchronous callback would couple this service's availability to PatientService's.
