# E2ETests

Selenium end-to-end tests that drive the real frontend and the whole backend stack through
the Gateway in a Chrome browser. Sprint 1 coverage (User Login, User Logout, and the patient
and user-management screens) is tracked under
[SWC-60](https://swiftcare-app.atlassian.net/browse/SWC-60); Sprint 2 adds browser coverage
for SWC-15, SWC-18, SWC-20, SWC-21, SWC-22, SWC-23 and SWC-24 plus one cross-story clinic-day
journey, tracked under SPRINT2-QA-01.
Sprint 3 adds SWC-25 vital-sign form, SWC-28 medical-alert, combined SWC-26/SWC-38
consultation-completion and combined SWC-29/SWC-40 prescription coverage under SWC-110.

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

The suite uses bounded collection-level parallelism. Independent test classes run with a
default maximum of two conservative workers. Workflows that select from or observe the
clinic-wide queue belong to the `Shared queue E2E` collection; xUnit runs that collection
without overlapping any other test. This prevents `Call Next` from consuming another
test's patient while still allowing authentication and isolated profile tests to overlap.

Override the worker limit for one run through xUnit's supported VSTest settings:

```bash
# Three bounded workers
dotnet test tests/E2ETests -- xUnit.MaxParallelThreads=3

# Diagnostic sequential run
dotnet test tests/E2ETests -- xUnit.ParallelizeTestCollections=false
```

In PowerShell, visible Chrome execution uses the same concurrency controls:

```powershell
$env:E2E_HEADLESS = "false"
dotnet test tests/E2ETests -- xUnit.MaxParallelThreads=2
```

Environment variables:

| Variable | Default | Purpose |
| --- | --- | --- |
| `AUTH_SEED_PASSWORD` | *(required)* | Password for the seeded accounts |
| `E2E_BASE_URL` | `http://localhost:5173` | Frontend URL to drive |
| `E2E_HEADLESS` | `true` | Set to `false` to watch the browser locally |
| `E2E_GATEWAY_URL` | `http://localhost:8000` | Gateway the tests seed preconditions through |
| `MYSQL_PASSWORD` | *(required)* | Password used for isolated QueueService test-data cleanup |
| `MYSQL_USER` | `swiftcare` | User for the QueueService database |
| `MYSQL_PORT` | `3306` | Published MySQL port |
| `QUEUE_DB_NAME` | `swiftcare_queue` | QueueService database name |
| `E2E_QUEUE_DB_HOST` | `localhost` | Host MySQL is published on |
| `E2E_QUEUE_DB_CONNECTION` | *(unset)* | Full connection string, overrides the four above |
| `MEDICAL_RECORD_DB_NAME` | `swiftcare_medical_record` | MedicalRecordService database name |
| `E2E_MEDICAL_RECORD_DB_HOST` | `localhost` | Host used by the SWC-28 historical follow-up fixture |
| `E2E_MEDICAL_RECORD_DB_CONNECTION` | *(unset)* | Full MedicalRecordService connection string override |
| `PRESCRIPTION_DB_NAME` | `swiftcare_prescription` | PrescriptionService database name |
| `E2E_PRESCRIPTION_DB_HOST` | `localhost` | Host used by the SWC-40 dispensed-prescription fixture |
| `E2E_PRESCRIPTION_DB_CONNECTION` | *(unset)* | Full PrescriptionService connection string override |

### Why the suite touches MySQL directly

The SWC-15 check-in tests and clinic-day journey need the state "a patient exists but is not
in today's queue". Registering a patient publishes `patient-checked-in`, while no shipped
endpoint removes or completes a queue entry. Bounded parallel execution also requires
profile/search tests to remove that incidental queue row so a later `Call Next` cannot
consume it. `Support/QueueDatabase.cs` therefore deletes one row at a time, identified by a
patient id created by that test. It never clears the queue or deletes another test's data.
Set `MYSQL_PASSWORD` from the repo root `.env` alongside `AUTH_SEED_PASSWORD`.

SWC-28 must display an already overdue follow-up, while SWC-122 correctly rejects a doctor
entering a past date and the application has no test clock. The alert test creates and
completes a consultation through the real APIs with a valid date, then
`Support/MedicalRecordDatabase.cs` backdates only that test-owned row by consultation and
patient id. It never changes another consultation or bypasses the browser behavior under
test.

SWC-40 makes a DISPENSED prescription read-only, but no API can dispense a prescription
until SWC-41. The prescription test saves a prescription through the real API, then
`Support/PrescriptionDatabase.cs` marks only that test-owned PENDING row as DISPENSED by
prescription and patient id before the browser checks the read-only view.

## Parallel-safety classification

| Classification | Test classes | Execution |
| --- | --- | --- |
| Global queue | `CheckInPatientTests`, `FullQueueTests`, `WaitingPoolTests`, `CallNextPatientTests`, `CurrentPatientProfileTests`, `WaitingRoomDisplayTests`, `ConsultationTests`, `VitalSignsTests`, `MedicalAlertBannerTests`, `ConsultationCompletionTests`, `PrescriptionTests`, `ClinicDayJourneyTests` | Exclusive `Shared queue E2E` collection |
| Isolated patient/profile | Allergy, chronic-condition, search, registration and receptionist journey tests | Up to the configured worker limit; registration-created queue rows are removed by patient id |
| Independent identity/UI | Login, logout, user-management and admin journey tests | Up to the configured worker limit |

Every generated value is tagged through `TestData.RunId`, which includes the test-process
id so two suite processes started in the same second remain distinct. `SeedClient` removes
each API-seeded patient's remaining queue row on disposal. UI registration tests resolve
their uniquely generated patient name and remove that patient's row explicitly. Cleanup
never truncates tables or depends on broad date-based deletion.
Current-patient profile tests now create a real Call Next assignment and include a
fresh-Chrome-session check. They fail before calling if an older waiting entry would
be selected, rather than consuming a patient outside the test's own data.

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
MedicalRecordService, PrescriptionService and Gateway from the main Compose file.
It starts MySQL and Kafka, applies each service's migrations with its `--migrate`
command, then creates the Kafka topics. All five backend services must pass their
Compose health checks before Gateway starts; Gateway must be healthy before
Selenium runs. The job supplies a disposable MySQL user/password to both Compose
and the test process so the database helpers connect to the same databases.

On failure, the `e2e-test-results` artifact contains Compose status, logs from
every backend service including PrescriptionService, frontend output and test
results. CI removes its temporary containers and database volume
afterward.
