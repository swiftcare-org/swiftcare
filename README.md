# SwiftCare

SwiftCare is a healthcare queue and medical-record management system built as six independently deployable microservices. It supports authentication, patient administration, clinical records, prescriptions, queue operations, notifications, and reporting.

## Features

- JWT authentication and role-based authorization
- Patient registration, search, and profile management
- Queue creation, check-in, calling, and waiting-room display
- Medical histories, consultations, allergies, conditions, and vital signs
- Prescription creation, dispensing, and history
- Notifications, activity feeds, and operational reports
- Kafka-based asynchronous communication between services

## Technology stack

- **Frontend:** React, JavaScript or TypeScript, and npm
- **Backend:** ASP.NET Core Web API, .NET, REST, JWT, and xUnit
- **Data:** Entity Framework Core, Pomelo MySQL provider, and MySQL
- **Messaging:** Apache Kafka and ZooKeeper
- **DevOps:** GitHub Actions, Docker, Docker Compose, Microsoft Azure, GitHub Environments, and GitHub Actions Secrets
- **Operations:** Health checks, structured logging, correlation IDs, and service-level telemetry

## Microservices

| Service | Responsibility |
| --- | --- |
| AuthService | Accounts, login, logout, JWT authentication, users, and roles |
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

Application projects have not yet been scaffolded. Placeholder directories reserve the agreed layout.

## Prerequisites

- Git
- Docker Desktop with Docker Compose
- .NET SDK after backend projects are scaffolded
- Node.js and npm after the frontend project is scaffolded

The team must agree on and document exact .NET and Node.js versions when project scaffolding begins.

## Planned ports

| Component | Port |
| --- | ---: |
| React frontend | 5173 |
| AuthService | 5000 |
| PrescriptionService | 5001 |
| PatientService | 5002 |
| QueueService | 5003 |
| MedicalRecordService | 5004 |
| NotificationService | 5005 |
| MySQL | 3306 |
| Kafka | 9092 |
| ZooKeeper | 2181 |

## Local setup

Clone the repository and copy the environment template:

```bash
git clone https://github.com/swiftcare-org/swiftcare.git
cd swiftcare
cp .env.example .env
```

Replace the local placeholder passwords and JWT signing key in `.env`. Never commit `.env`.

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

Both permanent branches require pull requests, at least one approval, resolved conversations, and blocked force pushes and deletion. CI checks become required only after the workflow has completed successfully at least once.

Branch names use the exact Jira issue:

```text
feature/SWC-6-user-login
fix/SWC-12-fix-patient-search
refactor/SWC-6-improve-authentication
docs/SWC-9-update-registration-documentation
test/SWC-19-add-queue-tests
```

## Commit convention

Allowed types are `feat`, `fix`, `refactor`, `docs`, `test`, `style`, `perf`, and `chore`.

```text
feat(SWC-6): implement user login
fix(SWC-12): correct patient search validation
test(SWC-19): add queue event tests
```

## Pull-request workflow

1. Create a Jira-linked branch from `develop`.
2. Commit changes using the agreed convention.
3. Push the branch and open a pull request into `develop`.
4. Obtain at least one approval, resolve conversations, and pass required CI checks.
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
- `KAFKA_BOOTSTRAP_SERVERS`

Create GitHub Environments named `development`, `staging`, and `production` when deployment resources exist. Production must require an authorized reviewer. Do not create fake values merely to reserve secret names.

## CI/CD

The initial GitHub Actions workflow validates required foundation files and Docker Compose configuration on pushes and pull requests involving `develop` or `main`. It deliberately does not run npm or .NET commands before application projects exist.

CI will later add frontend linting, tests, builds, backend restore/build/xUnit coverage, EF Core migration validation, container image builds, security checks, registry publishing, Azure staging deployment, and post-deployment `/health` checks.

## Observability policy

Every service must eventually expose `/health`, emit structured logs, measure request failures and latency, record Kafka producer and consumer failures, and propagate correlation IDs through HTTP calls and Kafka events.

Logs must never include passwords, JWT tokens, database connection strings, access credentials, or sensitive patient and medical information.

## Current status

The repository and local infrastructure foundation are being established. React and ASP.NET Core projects, application containers, service Swagger endpoints, and Azure deployment URLs will be added by the development and DevOps teams as implementation progresses.
