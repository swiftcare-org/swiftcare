# NotificationService

Keeps a record of what is happening in the department and serves it as the live activity feed. NotificationService owns the `swiftcare_notification` database exclusively. No other service may query or write to it.

## What it does

- Consumes three Kafka topics with one consumer: `patient-checked-in` (PatientService), `patient-called` (QueueService) and `consultation-completed` (MedicalRecordService).
- Stores each event as one row in `Notifications`. An event is stored before its offset is committed, so a crash between the two redelivers the event instead of losing it.
- Idempotent against Kafka's at-least-once delivery: `EventId` has a unique index, so a redelivered event is recognised and skipped. Two instances storing the same event at the same moment are settled by that index.
- An event that cannot be read (invalid JSON, a missing identifier, text longer than its column) is logged and skipped, because retrying it would never succeed. A storage failure is retried by seeking back to the same offset after `Kafka:RetryDelay`.
- `GET /api/notifications?limit=50` returns the most recent events, newest first. `limit` defaults to 50 and is clamped to 1 to 200. Receptionist and Admin only.
- `GET /api/reports/daily?date=yyyy-MM-dd` returns the daily summary for one clinic day: total, new and returning patients (each patient who checked in is counted once), patients called to each room (Rooms 1, 2 and 3 are always listed) and the five most common diagnoses. A day with no activity returns zero totals. A missing or invalid date returns `400`. Admin only.
- `GET /api/reports/monthly?month=yyyy-MM` returns the monthly summary for one calendar month of clinic days: total, new and returning patients (each patient counted once in the month), the five most common diagnoses, and a weekly breakdown. Week 1 is days 1 to 7, Week 2 days 8 to 14, Week 3 days 15 to 21 and Week 4 day 22 to the end of the month. A patient is counted in the week of their first visit that month, so the four weeks add up to the total. A month with no activity returns zero totals and four weeks at 0. A missing or invalid month returns `400`. Admin only.
- `GET /health` is the liveness and readiness check.
- Enforces the Gateway trust boundary via `GatewaySecretMiddleware`, matching every other service.

## What it stores, and what it does not

The Kafka events carry no patient name, symptoms or clinical notes, so this service never holds them. A notification holds the event ID, its type, the patient ID, the event time, for `patient-called` the queue number, doctor name and room number, and for `consultation-completed` the diagnosis, which the reports count. A diagnosis longer than 200 characters is shortened to fit. Consultations completed before the event carried a diagnosis are stored without one and are left out of the diagnosis counts.

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

`Kafka:PatientCheckedInTopic` (`patient-checked-in`), `Kafka:PatientCalledTopic` (`patient-called`), `Kafka:ConsultationCompletedTopic` (`consultation-completed`), `Kafka:ConsumerGroupId` (`notification-service`), `Kafka:RetryDelay`, `Reports:ClinicTimeZone` (`Asia/Colombo`) and `Reports:Rooms` (`1`, `2`, `3`) are non-secret settings.

Never hardcode these values in source or commit them to `.env`.

## Database migrations

Migrations are not applied at startup. Run the service once with `--migrate` against a new database:

```bash
dotnet run --project services/NotificationService -- --migrate
```

## Deployment status

The API Gateway routes for `/api/notifications`, `/api/reports/daily` and `/api/reports/monthly` exist and point at `http://localhost:5005` by default. The Dockerfile, Docker Compose entry (including the Gateway's address for this service inside Docker), CI and CD jobs and infrastructure are delivered by SWC-133. Until then the service runs with `dotnet run`.

## Tests

```bash
dotnet test tests/NotificationService.UnitTests
```
