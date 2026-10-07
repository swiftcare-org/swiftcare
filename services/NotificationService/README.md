# NotificationService

Keeps a record of what is happening in the department and serves it as the live activity feed. NotificationService owns the `swiftcare_notification` database exclusively. No other service may query or write to it.

## What it does

- Consumes three Kafka topics with one consumer: `patient-checked-in` (PatientService), `patient-called` (QueueService) and `consultation-completed` (MedicalRecordService).
- Stores each event as one row in `Notifications`. An event is stored before its offset is committed, so a crash between the two redelivers the event instead of losing it.
- Idempotent against Kafka's at-least-once delivery: `EventId` has a unique index, so a redelivered event is recognised and skipped. Two instances storing the same event at the same moment are settled by that index.
- An event that cannot be read (invalid JSON, a missing identifier, text longer than its column) is logged and skipped, because retrying it would never succeed. A storage failure is retried by seeking back to the same offset after `Kafka:RetryDelay`.
- `GET /api/notifications?limit=50` returns the most recent events, newest first. `limit` defaults to 50 and is clamped to 1 to 200. Receptionist and Admin only.
- `GET /health` is the liveness and readiness check.
- Enforces the Gateway trust boundary via `GatewaySecretMiddleware`, matching every other service.

## What it stores, and what it does not

The Kafka events carry identifiers only, by design, so this service never holds a patient name or any clinical detail. A notification holds the event ID, its type, the patient ID, the event time and, for `patient-called`, the queue number, doctor name and room number.

The activity feed page resolves patient names through PatientService's existing patient-profile endpoint and caches successful lookups between polls. This keeps personal information with the service that owns it. NotificationService does not call any other service.

`consultation-completed` carries no timestamp of its own, so that event is timed by when it arrived.

## Port

`5005` (see `Properties/launchSettings.json`).

## Dependencies

- .NET 10 SDK
- MySQL 8.4 reachable at the connection string in `ConnectionStrings:NotificationDb`
- EF Core 9 / Pomelo MySQL provider (pinned below EF Core 10 until Pomelo releases `net10` support)
- A reachable Kafka broker with the three topics above. The broker does not need to be reachable for the service to start; a lost connection is logged and retried and does not block `/health`
- `dotnet-ef` as a local tool (already restored via the repository's `dotnet-tools.json`)

## Required environment variables

NotificationService fails fast at startup if any of these are missing.

| Variable | Purpose | Notes |
| --- | --- | --- |
| `ConnectionStrings__NotificationDb` | MySQL connection string for `swiftcare_notification` | Required |
| `Gateway__InternalSecret` | Shared secret validated on every non-health request | Required, must match the API Gateway's `Gateway__InternalSecret` |
| `Kafka__BootstrapServers` | Address of the Kafka broker | Required to be configured |

`Kafka:PatientCheckedInTopic` (`patient-checked-in`), `Kafka:PatientCalledTopic` (`patient-called`), `Kafka:ConsultationCompletedTopic` (`consultation-completed`), `Kafka:ConsumerGroupId` (`notification-service`) and `Kafka:RetryDelay` are non-secret settings.

Never hardcode these values in source or commit them to `.env`.

## Database migrations

Migrations are not applied at startup. Run the service once with `--migrate` against a new database:

```bash
dotnet run --project services/NotificationService -- --migrate
```

## Deployment status

The Dockerfile, Docker Compose entry, CI and CD jobs, infrastructure and the API Gateway route for `/api/notifications` are delivered by SWC-133. Until then the service runs with `dotnet run` and the activity feed page cannot reach it through the Gateway.

## Tests

```bash
dotnet test tests/NotificationService.UnitTests
```
