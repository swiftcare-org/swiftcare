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

`ApiGateway/`, `services/AuthService/`, and `frontend/` are scaffolded and implement SWC-6 (user login). The remaining services under `services/` are still placeholder directories reserving the agreed layout.

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

The initial Compose stack starts MySQL, Kafka, and ZooKeeper only. Application containers will be added after projects and Dockerfiles exist.

## Branching strategy

- `main` contains stable, reviewed, releasable code.
- `develop` integrates completed user stories.
- Feature branches start from `develop` and merge back through pull requests.
- Only `develop` should normally merge into `main`.

Both permanent branches require pull requests, at least two approvals from teammates other than the author, resolved conversations, and blocked force pushes and deletion. Prefer requesting that sprint's DevOps and QA role holders first, but any two teammates may approve. CI checks become required only after the workflow has completed successfully at least once.

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
4. Obtain two approvals from teammates other than the author (prefer that sprint's DevOps and QA role holders), resolve conversations, and pass required CI checks.
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

Planned GitHub Actions secrets are:

- `AZURE_CREDENTIALS`
- `AZURE_SUBSCRIPTION_ID`
- `AZURE_RESOURCE_GROUP`
- `AZURE_CONTAINER_REGISTRY`
- `AZURE_REGISTRY_USERNAME`
- `AZURE_REGISTRY_PASSWORD`
- `AZURE_WEBAPP_NAME`
- `MYSQL_CONNECTION_STRING`
- `JWT_SIGNING_KEY`
- `GATEWAY_INTERNAL_SECRET`
- `KAFKA_BOOTSTRAP_SERVERS`

Create GitHub Environments named `development`, `staging`, and `production` when deployment resources exist. Production must require an authorized reviewer. Do not create fake values merely to reserve secret names.

## CI/CD

The GitHub Actions workflow runs on pushes and pull requests involving `develop` or `main`, as three jobs:

- **Validate repository infrastructure** — confirms required foundation files exist and validates `docker-compose.yml`.
- **Build and test .NET projects** — restores, builds, and runs xUnit tests against `SwiftCare.slnx` (all services, the API Gateway, and their test projects), with coverage collected via coverlet and uploaded as a build artifact.
- **Build and lint frontend** — `npm ci`, `npm run lint` (oxlint), and `npm run build` against `frontend/`.

The .NET and frontend jobs each guard on their respective entry file (`SwiftCare.slnx`, `frontend/package.json`) existing, so they no-op harmlessly on branches that don't have those projects yet rather than failing.

CI will later add EF Core migration validation, container image builds, security checks, registry publishing, Azure staging deployment, and post-deployment `/health` checks. The pipeline will build, image, and deploy the API Gateway with the six microservices, and its `/health` endpoint must pass before a deployment is promoted.

## Observability policy

Every service must eventually expose `/health`, emit structured logs, measure request failures and latency, record Kafka producer and consumer failures, and propagate correlation IDs through HTTP calls and Kafka events.

The API Gateway must expose `/health`, log incoming requests with correlation IDs, and record JWT validation failures, gateway-authentication failures, and routing errors. It must remove client-supplied identity and gateway-secret headers before attaching trusted forwarding headers.

Logs must never include passwords, JWT tokens, database connection strings, access credentials, or sensitive patient and medical information.
