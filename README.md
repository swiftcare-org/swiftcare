# SwiftCare

SwiftCare is a healthcare queue and medical-record management system built as six independently deployable microservices behind an API Gateway. It supports authentication, patient administration, clinical records, prescriptions, queue operations, notifications, and reporting.

## Features

- API Gateway with centralized JWT authentication and role-based authorization
- Patient registration, search, and profile management
- Queue creation, check-in, calling, and waiting-room display
- Medical histories, consultations, allergies, conditions, and vital signs
- Prescription creation, dispensing, and history
- Notifications, activity feeds, and operational reports
- Kafka-based asynchronous communication between services

## Technology stack

- **Frontend:** React 19, TypeScript, Vite 8, Tailwind CSS v4, React Router 7, and npm
- **Backend:** ASP.NET Core Web API on .NET 10, REST, JWT, xUnit, and YARP for the API Gateway
- **Data:** Entity Framework Core 9 (Pomelo MySQL provider), and MySQL
- **Messaging:** Apache Kafka and ZooKeeper
- **DevOps:** GitHub Actions, Docker, Docker Compose, Microsoft Azure, GitHub Environments, and GitHub Actions Secrets
- **Operations:** Health checks, structured logging, correlation IDs, and service-level telemetry

### Pinned framework versions

| Component | Version | Notes |
| --- | --- | --- |
| .NET SDK | **10.0** | Only SDK available on the original dev machine at scaffolding time; overrides an earlier .NET 8 LTS assumption |
| EF Core | **9.0** | Pinned below the SDK's default (10) because Pomelo's MySQL provider has not released `net10` support yet |
| React | **19** | Scaffolded via `npm create vite@latest -- --template react-ts` |
| Node.js | **22.x** | Required to run the frontend tooling |

Keep the .NET SDK and EF Core pins in sync across every service — do not let one service drift onto a different EF Core major version than the others.

## Microservices

The API Gateway is the single entry point for frontend requests. It validates JWTs, applies CORS, routes requests, removes untrusted forwarding headers, and supplies trusted user identity and gateway-authentication headers to the services.

| Service | Responsibility |
| --- | --- |
| AuthService | Accounts, login, logout, JWT issuance, password management, users, and roles |
| PatientService | Patient registration, search, profiles, and patient information |
| QueueService | Queue entry creation, check-in, queue management, calling patients, and public display |
| MedicalRecordService | Allergies, chronic conditions, consultations, vital signs, and medical history |
| PrescriptionService | Prescriptions, medicines, dispensing, and prescription history |
| NotificationService | Activity feed, notifications, and applicable daily or monthly reports |

Sprint 1 focuses on AuthService, PatientService, and QueueService.

## Repository structure

```text
swiftcare/
|-- .github/workflows/ci.yml
|-- .github/workflows/cd.yml
|-- ApiGateway/
|-- frontend/
|-- services/
|   |-- AuthService/
|   |-- PatientService/
|   |-- QueueService/
|   |-- MedicalRecordService/
|   |-- PrescriptionService/
|   `-- NotificationService/
|-- deployment/
|   |-- docker/
|   `-- azure/
|-- tests/
|-- docs/
|-- .env.example
|-- .gitignore
|-- docker-compose.yml
`-- README.md
```

`ApiGateway/`, `frontend/`, and the AuthService, PatientService, QueueService, MedicalRecordService, and PrescriptionService projects contain implemented application slices. NotificationService remains a placeholder directory reserving the agreed layout.

## Prerequisites

- Git
- Docker Desktop with Docker Compose
- .NET SDK 10.0
- Node.js 22.x and npm

See [Pinned framework versions](#pinned-framework-versions) above.

## Planned ports

| Component | Port |
| --- | ---: |
| React frontend | 5173 |
| API Gateway | 8000 |
| AuthService | 5000 |
| PrescriptionService | 5001 |
| PatientService | 5002 |
| QueueService | 5003 |
| MedicalRecordService | 5004 |
| NotificationService | 5005 |
| MySQL | 3306 |
| Kafka | 9092 |
| ZooKeeper | 2181 |

The frontend communicates exclusively with the API Gateway on port `8000`. Individual service ports are reserved for internal Docker network communication and local development access only.

## Local setup

Clone the repository and copy the environment template:

```bash
git clone https://github.com/swiftcare-org/swiftcare.git
cd swiftcare
cp .env.example .env
```

Replace the local placeholder passwords, JWT signing material, and gateway internal secret in `.env`. AuthService issues JWTs, while the API Gateway validates them before forwarding protected requests. The gateway and services use `GATEWAY_INTERNAL_SECRET` to reject requests that did not originate from the gateway. Never commit `.env`.

Start and inspect the local infrastructure:

```bash
docker compose up -d
docker compose ps
docker compose logs -f
```

Stop the containers:

```bash
docker compose down
```

To stop containers and remove local volumes:

```bash
docker compose down -v
```

**Warning:** `docker compose down -v` permanently deletes local database data stored in Compose volumes.

The Compose stack starts MySQL 8.4, ZooKeeper, ZooKeeper-backed Confluent Kafka 7.6.1, Kafka UI, AuthService, PatientService, QueueService, MedicalRecordService, and the API Gateway. The frontend still runs through Vite on the host. Kafka uses `kafka:29092` inside the Compose network and `localhost:9092` for host tools.

## Running the application locally

For the closest match to deployment, start MySQL and Kafka first, apply migrations through each backend service's normal image, then start the full stack, including the API Gateway, and run the frontend on the host. Compose does not apply migrations automatically for any service:

```bash
docker compose up -d mysql kafka
docker compose run --rm --no-deps authservice --migrate
docker compose run --rm --no-deps patientservice --migrate
docker compose run --rm --no-deps queueservice --migrate
docker compose run --rm --no-deps medicalrecordservice --migrate
docker compose up -d
```

`--no-deps` does not start a service's dependencies, so MySQL must already be running before any of these `--migrate` commands, which is why it is started first above. The host-based .NET commands below remain useful while actively developing a service; stop the corresponding Compose application container before using its host port.

### One-time database preparation

On an empty MySQL volume, `deployment/docker/mysql-init/01-create-databases.sh` creates all six service-owned databases and grants the local `MYSQL_USER` access to them. The script runs automatically through `/docker-entrypoint-initdb.d`; it does not run again against an existing volume.

### Apply migrations

```bash
dotnet tool restore
dotnet ef database update --project services/AuthService --connection "Server=localhost;Port=3306;Database=swiftcare_auth;User Id=<MYSQL_USER>;Password=<MYSQL_PASSWORD>;"
dotnet ef database update --project services/PatientService --connection "Server=localhost;Port=3306;Database=swiftcare_patient;User Id=<MYSQL_USER>;Password=<MYSQL_PASSWORD>;"
dotnet ef database update --project services/QueueService --connection "Server=localhost;Port=3306;Database=swiftcare_queue;User Id=<MYSQL_USER>;Password=<MYSQL_PASSWORD>;"
ConnectionStrings__MedicalRecordDb="Server=localhost;Port=3306;Database=swiftcare_medical_record;User Id=<MYSQL_USER>;Password=<MYSQL_PASSWORD>;" dotnet run --project services/MedicalRecordService -- --migrate
dotnet ef database update --project services/PrescriptionService --connection "Server=localhost;Port=3306;Database=swiftcare_prescription;User Id=<MYSQL_USER>;Password=<MYSQL_PASSWORD>;"
```

`--connection` is required for the direct `dotnet ef` commands. Each service's design-time DbContext factory supplies placeholder credentials so that `migrations add` never contacts a live database, and EF prefers that factory over the application host — without an explicit connection the command authenticates as a user that does not exist.

MedicalRecordService uses its `--migrate` maintenance command through the same normal application image, following the pattern documented in the [MedicalRecordService migration guide](services/MedicalRecordService/README.md#ef-core-migrations).

Re-run these commands after pulling any change that adds a migration.

### Complete a consultation

After a doctor saves a consultation and its vital signs, Complete Consultation writes `COMPLETE` and a stable event ID to the MedicalRecord database before publishing `consultation-completed` to Kafka. QueueService consumes that event, changes the matching queue entry from `IN_CONSULTATION` to `COMPLETED`, and stores its UTC completion time. The completed entry disappears from the doctor's current-patient card. Once the doctor saves a prescription, the receptionist queue links the completed entry to its prescription details. If publishing fails, the doctor sees a retry message; the consultation remains complete in the database and retry republishes the same event ID. QueueService ignores duplicate deliveries with its `ProcessedEvents` ledger and preserves the original completion time. On success, the doctor proceeds to the prescription form with the consultation, queue, and patient identifiers. See the [MedicalRecordService flow](services/MedicalRecordService/README.md#completing-a-consultation) for completion behavior.

### Create a prescription

After a successful consultation, a doctor can add multiple medicines with a name, dosage, frequency, duration, and optional instructions. The form shows PatientService allergy information as an advisory warning and loads the patient's previous prescriptions for reference. `POST /api/prescriptions` stores the consultation, queue, patient, and trusted doctor identifiers with a default `PENDING` status. After saving, the prescribing doctor can add medicines or remove a confirmed item while keeping at least one medicine. A `DISPENSED` prescription is read-only. A unique consultation index prevents a second prescription for the same consultation. See the [PrescriptionService guide](services/PrescriptionService/README.md) for endpoints, configuration, and local startup.

### View prescription history

A doctor sees a patient's past prescriptions in two places. The patient history page, opened with **View History** on the patient profile, has a Prescription History section listing every prescription newest first with its date, doctor, medicines, dosages and `PENDING` or `DISPENSED` status. The prescription form shows the same list beside the form on wide screens, and below it on smaller ones, so earlier medicines can be checked while prescribing; the prescription being written is labelled `This visit`. A patient without prescriptions sees `No prescriptions recorded for this patient`. Administrators can read daily totals from `GET /api/prescriptions/report/daily`, which the daily summary report uses. See the [PrescriptionService guide](services/PrescriptionService/README.md#prescription-history-and-daily-report) for the endpoints.

### View prescriptions at the counter

The receptionist queue shows today's pending prescriptions in prescription-time order so the oldest is handled first. Each link opens the full counter view with the patient name, queue number, date, doctor, room, status, and all medicine details. If the consultation is complete but the doctor has not saved a prescription, the page explains that the doctor may still be writing it instead of displaying an error. When no pending prescriptions remain, the queue shows `All prescriptions dispensed today`.

### Dispense a prescription

Receptionists open a completed queue entry's prescription details and use **Mark as Dispensed** while its status is `PENDING`. PrescriptionService changes the status to `DISPENSED` and records the trusted receptionist name and current UTC timestamp. The details page then shows who dispensed it with the clinic-local date and time, and the queue displays a `DISPENSED` badge. Doctor and Admin access is read-only, and an already dispensed prescription cannot be dispensed again.

### Medical alerts and follow-ups

A doctor can save an optional follow-up date and instructions with a consultation. Both values must be supplied together. When a doctor opens a patient profile, SwiftCare displays one red alert per allergy, followed by one amber alert per chronic condition. If the patient's latest completed consultation has a follow-up date earlier than the current `Asia/Colombo` clinic date, a blue overdue follow-up alert appears last. It names the doctor who recorded the follow-up and says how long it is overdue, for example `Dr. Silva noted: Review blood pressure in 6 weeks (overdue by 3 weeks)`. Under two weeks the delay is shown in days, under eight weeks in weeks, and after that in months. The doctor-only `GET /api/consultations/patient/{patientId}/latest-follow-up` endpoint returns `204 No Content` when no overdue follow-up exists.

### View patient history

A doctor can open a patient's history from the **View History** button on the patient profile, at `/patients/{patientId}/history`. The page lists the patient's completed consultations from every doctor, newest first, with the date, doctor, symptoms, diagnosis and a short notes summary. Each entry expands to show the full detail, including examination findings and follow-up instructions. Below the timeline, the vitals history lists every recorded reading by date with an arrow beside each measurement showing whether it rose or fell against the previous recorded value. A first-time patient sees `First visit. No previous consultations.`, and a patient without readings sees `No vital signs recorded yet`. The page is available to doctors only. See the [MedicalRecordService guide](services/MedicalRecordService/README.md#patient-history) for the endpoints.

### Load configuration

Both .NET processes fail fast when configuration is missing, and several values must be **identical** across them — `Jwt__SecretKey`, `Jwt__Issuer`, `Jwt__Audience`, and `Gateway__InternalSecret`. A mismatch produces a `401` that the login page reports as invalid credentials, so derive them all from `.env` rather than typing them.

Run this at the top of each terminal (PowerShell):

```powershell
Get-Content .env | ForEach-Object {
    if ($_ -match '^\s*([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.*)$') {
        Set-Item -Path "Env:$($matches[1])" -Value $matches[2].Trim()
    }
}

$env:Jwt__SecretKey          = $env:JWT_SIGNING_KEY
$env:Jwt__Issuer             = $env:JWT_ISSUER
$env:Jwt__Audience           = $env:JWT_AUDIENCE
$env:Gateway__InternalSecret = $env:GATEWAY_INTERNAL_SECRET
$env:ASPNETCORE_ENVIRONMENT  = "Development"
```

### Start the processes

**Terminal 1 — AuthService** (port 5000):

```powershell
$env:ConnectionStrings__AuthDb = "Server=localhost;Port=3306;Database=$env:AUTH_DB_NAME;User Id=$env:MYSQL_USER;Password=$env:MYSQL_PASSWORD;"
dotnet run --project services/AuthService
```

**Terminal 2 — API Gateway** (port 8000):

```powershell
dotnet run --project ApiGateway
```

**Terminal 3 — frontend** (port 5173). Vite reads env files from `frontend/`, not the repository root, and [`src/api/client.ts`](frontend/src/api/client.ts) throws on load when the value is absent. Create `frontend/.env` containing `VITE_GATEWAY_URL=http://localhost:8000`, then:

```bash
cd frontend
npm ci
npm run dev
```

### Verify

```bash
curl http://localhost:5000/health
curl http://localhost:8000/health
```

Open `http://localhost:5173` and sign in. `DevelopmentSeeder` creates four synthetic accounts on first run, all sharing the value of `AUTH_SEED_PASSWORD`:

| Username | Role | Notes |
| --- | --- | --- |
| `dr.chen` | Doctor | Room `R-204` |
| `reception.silva` | Receptionist | |
| `admin.fernando` | Admin | |
| `dr.rao` | Doctor | Deactivated — exercises the rejection path |

Seeding is skipped with a warning when `AUTH_SEED_PASSWORD` is unset, which leaves the database empty and makes every login fail. Synthetic data only — never real patient or staff records.

### Running tests

```bash
dotnet test SwiftCare.slnx
cd frontend && npm run lint && npm run build
```

## Branching strategy

- `main` contains stable, reviewed, releasable code.
- `develop` integrates completed user stories.
- Feature branches start from `develop` and merge back through pull requests.
- Only `develop` should normally merge into `main`.

Both permanent branches require pull requests, approval from the sprint’s QA role, resolved conversations, and blocked force pushes and deletion. CI checks become required only after the workflow has completed successfully at least once.

Branch names use the exact Jira issue:

```text
feature/SWC-6-user-login
fix/SWC-12-fix-patient-search
refactor/SWC-6-improve-authentication
docs/SWC-9-update-registration-documentation
test/SWC-19-add-queue-tests
```

Branches not tied to a story (repository administration, e.g. CI or documentation housekeeping) may omit the issue key:

```text
docs/standup-log-template
chore/ci-dotnet-build-test
```

## Commit convention

Allowed types are `feat`, `fix`, `refactor`, `docs`, `test`, `style`, `perf`, and `chore`.

```text
feat(SWC-6): implement user login
fix(SWC-12): correct patient search validation
test(SWC-19): add queue event tests
```

Commits not tied to a story may omit the scope the same way:

```text
docs: add daily stand-up log template
```

## Pull-request workflow

1. Create a Jira-linked branch from `develop`.
2. Commit changes using the agreed convention.
3. Push the branch and open a pull request into `develop`.
4. Obtain approval from that sprint’s QA role, resolve conversations, and pass required CI checks.
5. Merge completed work into `develop`.
6. Promote reviewed release candidates from `develop` to `main` through a separate pull request.

The GitHub for Jira connection should recognize Jira keys in branch names, commits, pull-request titles, and pull-request descriptions.

## EF Core data ownership

Each microservice owns its entities, DbContext, logical MySQL database, and committed `Migrations` directory:

| Service | DbContext | Database |
| --- | --- | --- |
| AuthService | AuthDbContext | swiftcare_auth |
| PatientService | PatientDbContext | swiftcare_patient |
| QueueService | QueueDbContext | swiftcare_queue |
| MedicalRecordService | MedicalRecordDbContext | swiftcare_medical_record |
| PrescriptionService | PrescriptionDbContext | swiftcare_prescription |
| NotificationService | NotificationDbContext | swiftcare_notification |

A service must never query or modify another service's database. Local and CI migrations may be automated after projects exist. Staging migrations are controlled deployment steps, and production migrations must not run automatically at container startup.

## Configuration and secrets

`.env.example` contains safe local placeholders. Real passwords, tokens, JWT keys, Azure credentials, and connection strings must remain in local `.env` files or approved secret stores.

The CD workflow reads deployment configuration from the `azure-development` GitHub Environment. Non-sensitive Azure resource identifiers, region, application/job/container names, endpoints, JWT issuer/audience, database username, frontend origins, and initial administrator display name belong in environment variables. Passwords, registry credentials, JWT signing material, gateway trust material, the Static Web Apps deployment token, and optional bootstrap credentials belong in environment secrets.

Azure authentication uses GitHub OIDC through `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, and `AZURE_SUBSCRIPTION_ID`; do not create a long-lived `AZURE_CREDENTIALS` secret. Custom-domain deployment requires `GATEWAY_ORIGIN`, `FRONTEND_ORIGIN`, and `FRONTEND_WWW_ORIGIN` to contain HTTPS origins without trailing slashes. AuthService, PatientService, and QueueService use separate non-administrator MySQL accounts restricted to `swiftcare_auth`, `swiftcare_patient`, and `swiftcare_queue` respectively, never the flexible-server administrator account.

PatientService deployment requires `AZURE_PATIENT_APP`, `AZURE_PATIENT_MIGRATE_JOB`, and `PATIENT_DB_USER` environment variables plus the `PATIENT_DB_PASSWORD` environment secret. QueueService deployment requires `AZURE_QUEUE_APP`, `AZURE_QUEUE_MIGRATE_JOB`, `QUEUE_DB_USER`, `KAFKA_PATIENT_CHECKED_IN_TOPIC`, and `KAFKA_QUEUE_CONSUMER_GROUP` environment variables plus the `QUEUE_DB_PASSWORD` environment secret. MedicalRecordService deployment requires `AZURE_MEDICAL_RECORD_APP` (for example, `swiftcare-medical-record`), `AZURE_MEDICAL_RECORD_MIGRATE_JOB` (for example, `swiftcare-medical-record-migrate`), and `MEDICAL_RECORD_DB_USER` environment variables plus the `MEDICAL_RECORD_DB_PASSWORD` environment secret. The medical-record account needs privileges only on `swiftcare_medical_record`; create that account inside the Azure VNet before running CD and keep its password in the `azure-development` GitHub Environment secret. Review and apply the Terraform plan that creates `swiftcare_medical_record` first. If the database already exists outside Terraform state, import it instead of attempting to create it again. Production must use a separate protected GitHub Environment and authorized reviewers when it is introduced.

## CI/CD

The CI workflow runs on pushes and pull requests involving `develop` or `main`, as seven parallel jobs. Any failing job blocks the merge.

- **Validate repository infrastructure** — confirms required foundation files exist and validates `docker-compose.yml` parses.
- **Build and test .NET projects** — verifies formatting with `dotnet format`, then builds and runs xUnit tests against `SwiftCare.slnx` (all services, the API Gateway, and their test projects) in Release, then reports and gates coverage. The formatting check runs before the build so a style failure reports in seconds rather than after the full test run.
- **Build and lint frontend** — `npm ci`, `npm run lint` (oxlint), and `npm run build` against `frontend/`.
- **Scan dependencies for vulnerabilities** — `dotnet list package --vulnerable --include-transitive` across the solution, and `npm audit --audit-level=high` for the frontend.
- **Validate EF Core migrations** — applies every migration to a clean MySQL 8.4 service container, then fails when the model has changed without a corresponding migration.
- **Analyze code** — CodeQL static analysis over C# and TypeScript, reporting into the repository's Security tab.
- **Build deployable images** — builds the API Gateway, AuthService, PatientService, and QueueService Dockerfiles without publishing them, catching container-only failures before deployment.

Runs are grouped by workflow and ref with `cancel-in-progress`, so pushing twice in quick succession cancels the superseded run instead of executing both. NuGet packages are cached across runs, keyed on the project files and `dotnet-tools.json`.

Every job guards on its entry file (`SwiftCare.slnx`, `frontend/package.json`, `frontend/package-lock.json`) existing, so each no-ops harmlessly on branches without those projects rather than failing.

### Coverage

Test counts per suite are parsed from the TRX output into the run's job summary. Coverage is collected with coverlet, rendered by ReportGenerator into the same summary, and published as a build artifact alongside the TRX results.

The build fails when line coverage drops below `MINIMUM_LINE_COVERAGE`, defined in the workflow and currently `55`. Generated EF migration files are excluded from the calculation — they are neither hand-written nor directly tested, and including them reports 37.6% where service code actually sits at 59.6%. Raise the threshold as services land with tests; it exists to catch regression, not to be aspirational.

### Security

Two layers run independently. **Dependency scanning** checks third-party packages: `dotnet list package` exits `0` even when it finds vulnerabilities, so that step matches on its output instead of its exit code, and `npm audit` is pinned to `--audit-level=high` so that low and moderate advisories in build-time tooling, which never ship to production, do not block merges. Both currently report clean, so they gate against regression rather than flagging existing debt.

**CodeQL** analyses first-party source instead — injection flaws, unsafe patterns, and hardcoded credentials in code we wrote. The C# build runs after CodeQL initialises so the extractor can trace the compilation; reordering those steps silently produces an empty analysis. Findings appear under the repository's Security tab.

### Code quality (SonarQube Cloud)

The **SonarQube Cloud analysis** job sends the .NET services, API Gateway and frontend to the hosted SonarQube Cloud project `swiftcare-org_swiftcare` on the Free plan. It runs after the .NET build and test job and reuses its coverage through a `SonarQube.xml` report produced by ReportGenerator, so the Cobertura report and the 55% coverage gate are unchanged. Migrations, generated files, dependencies and build or test output are excluded.

The Free plan analyzes one branch and pull requests into it. The project's main branch is `develop`, so the job runs on pushes to `develop` and on pull requests into `develop` from this repository; it is skipped on `main`, manual runs and fork pull requests. Results appear as a pull request comment and on the SonarQube Cloud dashboard. The job waits for the built-in **Sonar way** quality gate, which checks only new code, and fails when the gate fails. Custom gates and `main` analysis need a paid plan and are not used.

The job reads the repository Actions secret `SONAR_TOKEN` and the repository variables `SONAR_ORGANIZATION` and `SONAR_PROJECT_KEY`. Never commit or print the token.

#### Setup and GitHub integration

SonarQube Cloud is an external hosted service; nothing is deployed to Azure and the Free plan has no cost.

1. Sign in to SonarQube Cloud with GitHub and import the `swiftcare-org` GitHub organization, installing the SonarQube Cloud GitHub App on the `swiftcare` repository only.
2. Choose the Free plan and import the repository as project `swiftcare-org_swiftcare` (public, matching the repository).
3. Under Administration > Analysis method, turn **Automatic Analysis off**. CI-based analysis is required to import coverage, and both methods cannot run together.
4. Under Project > Branches, rename the SonarQube main branch to `develop`. The GitHub branches are not renamed.
5. Keep the **Sonar way** quality gate and the **Previous version** new-code setting.
6. Create a token under My Account > Security with an expiry date, store it as the repository Actions secret `SONAR_TOKEN`, and store the organization and project keys as repository variables.

#### Ownership and responsibilities

The Sprint 4 DevOps engineer owns the SonarQube Cloud organization, the project settings and the analysis token, and keeps at least one other team member as an organization administrator for continuity. The Free plan allows up to five organization members. Developers own fixing findings in their own new code; the quality gate does not require fixing historical findings.

#### Credential management

The token exists only in the repository Actions secret; it is not stored in the `azure-development` Environment because the CI job does not use one. GitHub masks it in logs, and fork pull requests never receive it. To rotate it, create a new token, update `SONAR_TOKEN`, confirm a green analysis on a pull request into `develop`, then revoke the old token. Rotate before expiry and whenever ownership changes. Do not paste tokens into issues, chat, screenshots or evidence.

#### Quality gate behavior

On a pull request, new code is the pull request diff; on `develop` it is everything since the new-code baseline. Sonar way fails when new code introduces issues, leaves security hotspots unreviewed, has coverage below 80% or has more than 3% duplicated lines. Coverage and duplication are not evaluated for very small changes. Frontend files have no coverage report, so frontend coverage is not measured.

#### Free-plan limitations

Only `develop` and pull requests into it are analyzed. `main` is not analyzed, so the gate protects `develop` before promotion and does not directly block the Azure deployment from `main`. Custom quality gates and quality profiles are not available.

#### Troubleshooting

- Job skipped: the event must be a push to `develop` or a pull request into `develop` from this repository, and the .NET build and test job must pass.
- Configuration error before scanning: the step names the missing secret or variable.
- `You are running CI analysis while Automatic Analysis is enabled`: turn Automatic Analysis off.
- Analysis rejected for a branch: the Free plan accepts only `develop` and pull requests into it.
- Coverage missing: confirm the `test-results` artifact contains `coverage-report/SonarQube.xml` and the log shows `Imported coverage data for N files`.
- Authentication error: the token has expired or been revoked; rotate it.
- Quality gate failed: open the SonarQube link in the job log or pull request comment, read the failed new-code condition, and fix the new code rather than weakening the gate.

### Continuous deployment

A successful CI run for `main` automatically deploys the shared Azure development environment. `workflow_dispatch` runs the same CI quality gate and can deploy any selected branch for testing. Both paths publish immutable Gateway, AuthService, PatientService, QueueService, and MedicalRecordService images to GHCR; run service EF migrations as finite Container Apps jobs inside the VNet; deploy the services with internal ingress, including MedicalRecordService on port `5004`; deploy the public Gateway last with internal HTTPS destinations for QueueService and MedicalRecordService; smoke-test health, authentication, patient routing, queue display routing, and protected queue access; and deploy the frontend to Azure Static Web Apps. The Gateway accepts both configured frontend custom-domain origins, while the frontend build uses `GATEWAY_ORIGIN` as its public API base URL.

CD sets both minimum and maximum replicas to `1` for Gateway, AuthService, PatientService, QueueService, and MedicalRecordService on both creation and update. Running apps therefore keep a replica available when idle, avoiding scale-from-zero startup delays. For the shutdown procedure, see [deployment cost controls](deployment/terraform/README.md#cost-controls).

The Azure messaging prerequisite follows the repository architecture: one private Azure Container Instances group contains separate ZooKeeper and Confluent Kafka 7.6.1 containers. Kafka connects to ZooKeeper at `localhost:2181` because containers in the same group share a network namespace. `KAFKA_BOOTSTRAP_SERVERS` must instead contain the private broker address reachable from the Container Apps environment; local Compose addresses such as `kafka:29092` are rejected. PrescriptionService deployment is separate from SWC-29, and the placeholder NotificationService is not fabricated or deployed until its project exists.

Application Insights is not configured by CD yet because the current .NET projects have no Application Insights or OpenTelemetry instrumentation package. Adding telemetry is an application change and should be completed with the observability work below rather than represented by unused environment variables.

## Observability policy

Every service must eventually expose `/health`, emit structured logs, measure request failures and latency, record Kafka producer and consumer failures, and propagate correlation IDs through HTTP calls and Kafka events.

The API Gateway must expose `/health`, log incoming requests with correlation IDs, and record JWT validation failures, gateway-authentication failures, and routing errors. It must remove client-supplied identity and gateway-secret headers before attaching trusted forwarding headers.

Logs must never include passwords, JWT tokens, database connection strings, access credentials, or sensitive patient and medical information.
