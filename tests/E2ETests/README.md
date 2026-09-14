# E2ETests

Selenium end-to-end tests that drive the real frontend and the whole backend stack through
the Gateway in a Chrome browser. Sprint 1 coverage (User Login, User Logout, and the patient
and user-management screens) is tracked under
[SWC-60](https://swiftcare-app.atlassian.net/browse/SWC-60); Sprint 2 adds browser coverage
for SWC-15, SWC-18, SWC-20, SWC-21, SWC-22, SWC-23 and SWC-24 plus one cross-story clinic-day
journey, tracked under SPRINT2-QA-01.

Unlike `AuthService.UnitTests` / `ApiGateway.UnitTests`, this project has no
`ProjectReference` to any service, and it only talks to whatever is already
running, over HTTP.

## Prerequisites

1. Backend stack, from the repo root. The Sprint 2 tests exercise patients, the queue and
   medical records, so the whole stack has to be up, not only auth:

   ```bash
   docker compose up -d --build
   ```

2. Frontend dev server:

   ```bash
   cd frontend
   npm ci
   npm run dev
   ```

3. `AUTH_SEED_PASSWORD` set in the shell running the tests, matching the
   value used to seed the AuthService database (see repo root `.env`). This
   is the password for all four development-seeded accounts, e.g. `dr.chen`.

## Running

```bash
AUTH_SEED_PASSWORD=<value from .env> dotnet test tests/E2ETests
```

Environment variables:

| Variable | Default | Purpose |
| --- | --- | --- |
| `AUTH_SEED_PASSWORD` | *(required)* | Password for the seeded accounts |
| `E2E_BASE_URL` | `http://localhost:5173` | Frontend URL to drive |
| `E2E_HEADLESS` | `true` | Set to `false` to watch the browser locally |
| `E2E_GATEWAY_URL` | `http://localhost:8000` | Gateway the tests seed preconditions through |
| `MYSQL_PASSWORD` | *(required for SWC-15)* | Password for the QueueService database |
| `MYSQL_USER` | `swiftcare` | User for the QueueService database |
| `MYSQL_PORT` | `3306` | Published MySQL port |
| `QUEUE_DB_NAME` | `swiftcare_queue` | QueueService database name |
| `E2E_QUEUE_DB_HOST` | `localhost` | Host MySQL is published on |
| `E2E_QUEUE_DB_CONNECTION` | *(unset)* | Full connection string, overrides the four above |

### Why one test touches MySQL directly

Every other test in this suite sets its preconditions up over HTTP through the Gateway. The
SWC-15 check-in tests and the clinic-day journey cannot: their starting state is "a patient
exists but is not in today's queue", and registering a patient publishes `patient-checked-in`
so QueueService queues them immediately, while no shipped endpoint removes or completes a
queue entry. `Support/QueueDatabase.cs` deletes that one row, for a patient the run
registered itself, and nothing else. Set `MYSQL_PASSWORD` from the repo root `.env` alongside
`AUTH_SEED_PASSWORD`, or those tests fail with an explanatory message rather than a
connection error.

## Test categories

Every test carries `Category=E2E`; the cross-story journeys also carry `Category=Smoke`.

```bash
dotnet test tests/E2ETests --filter Category=E2E
dotnet test tests/E2ETests --filter Category=Smoke
```

`WebDriverManager` resolves and downloads a matching `chromedriver` for the
locally installed Chrome automatically, so no manual driver setup is needed.

## CI setup

The CI E2E job builds AuthService, PatientService, QueueService,
MedicalRecordService and Gateway from the main Compose file. It starts MySQL and
Kafka, applies the three EF migration sets and the medical-record SQL schema,
then creates the Kafka topics. All four backend services must pass their Compose
health checks before Gateway starts; Gateway must be healthy before Selenium
runs. The job supplies a disposable MySQL user/password to both Compose and the
test process so the queue precondition helper connects to the same database.

On failure, the `e2e-test-results` artifact contains Compose status, logs from
QueueService and MedicalRecordService alongside the other services, frontend
output and test results. CI removes its temporary containers and database volume
afterward.
