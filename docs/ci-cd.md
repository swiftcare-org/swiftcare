# CI/CD reference

This document describes the SwiftCare continuous integration and deployment pipelines in detail. For an overview, see the [README](../README.md#cicd-and-environments). Azure infrastructure, cost controls and operational procedures are in the [Terraform README](../deployment/terraform/README.md).

## Continuous integration

The CI workflow runs on pushes and pull requests involving `develop` or `main`. Its jobs run in parallel where they can, and any failing required job blocks the merge.

| Job | What it checks |
| --- | --- |
| Validate repository infrastructure | Required foundation files exist, `docker-compose.yml` parses, and the CD release scripts in `deployment/cd` pass their lint and tests |
| Build and test .NET projects | `dotnet format` verification, then a Release build of `SwiftCare.slnx` and every unit test project, with coverage collected and gated |
| Build and lint frontend | `npm ci`, `npm run lint` (oxlint) and `npm run build` in `frontend/` |
| Run E2E tests | Builds the Compose stack, applies migrations and runs the Selenium suite against it in headless Chrome |
| Mutation testing | Stryker.NET per service on pull requests; see [Mutation testing](#mutation-testing-strykernet) |
| SonarQube Cloud analysis | Code quality, security and coverage of new code against the Sonar way quality gate; see [Code quality](#code-quality-sonarqube-cloud) |
| Validate EF Core migrations | Applies every migration to a clean MySQL 8.4 container, then fails when the model changed without a migration |
| Build deployable images | Builds the gateway and all six service Dockerfiles without publishing, catching container-only failures |
| Scan container image for CVEs | Trivy scans each built image and fails on fixable HIGH or CRITICAL vulnerabilities |
| Scan dependencies for vulnerabilities | `dotnet list package --vulnerable --include-transitive` and `npm audit --audit-level=high` |
| Scan for committed secrets | gitleaks over the full Git history |
| Analyze code | CodeQL over C# and TypeScript, reporting to the repository Security tab |

Runs are grouped by workflow and ref with `cancel-in-progress`, so pushing twice in quick succession cancels the superseded run instead of executing both. NuGet packages are cached across runs, keyed on the project files and `dotnet-tools.json`.

Every job guards on its entry file (`SwiftCare.slnx`, `frontend/package.json`, `frontend/package-lock.json`) existing, so each no-ops harmlessly on branches without those projects rather than failing.

### Coverage

Test counts per suite are parsed from the TRX output into the run's job summary. Coverage is collected with coverlet, rendered by ReportGenerator into the same summary, and published as a build artifact alongside the TRX results.

The build fails when line coverage drops below `MINIMUM_LINE_COVERAGE`, defined in the workflow and currently `55`. Generated EF migration files are excluded from the calculation, because they are neither hand-written nor directly tested, and including them reports 37.6% where service code actually sits at 59.6%. Raise the threshold as services land with tests; it exists to catch regression, not to be aspirational.

### Security

Two layers run independently. **Dependency scanning** checks third-party packages: `dotnet list package` exits `0` even when it finds vulnerabilities, so that step matches on its output instead of its exit code, and `npm audit` is pinned to `--audit-level=high` so that low and moderate advisories in build-time tooling, which never ship to production, do not block merges. Both currently report clean, so they gate against regression rather than flagging existing debt.

**CodeQL** analyses first-party source instead: injection flaws, unsafe patterns, and hardcoded credentials in code we wrote. The C# build runs after CodeQL initialises so the extractor can trace the compilation; reordering those steps silently produces an empty analysis. Findings appear under the repository's Security tab.

### Code quality (SonarQube Cloud)

The **SonarQube Cloud analysis** job sends the .NET services, API Gateway and frontend to the hosted SonarQube Cloud project `swiftcare-org_swiftcare` on the Free plan. It runs after the .NET build and test job and reuses its coverage through a `SonarQube.xml` report produced by ReportGenerator, so the Cobertura report and the 55% coverage gate are unchanged. Migrations, generated files, dependencies and build or test output are excluded. The frontend and the service `Data/` folders (ADO.NET repositories and EF `DbContext` classes that only run against MySQL) are excluded from the coverage calculation only, because no CI test produces coverage for them; they are still analyzed for bugs, code smells and security issues.

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

On a pull request, new code is the pull request diff; on `develop` it is everything since the new-code baseline. Sonar way fails when new code introduces issues, leaves security hotspots unreviewed, has coverage below 80% or has more than 3% duplicated lines. Coverage and duplication are not evaluated for very small changes. The coverage condition applies to backend code outside `Data/`; the frontend has no test runner, so its coverage is not measured.

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

### Mutation testing (Stryker.NET)

Owner: Kularatne P.O.N (IT24103437), SE3112 role Mutation Testing (SWC-151).

Mutation testing checks whether the unit tests would catch a bug: Stryker.NET makes small deliberate changes to the service code and reruns the unit tests against each one. It covers all seven backend services (ApiGateway, AuthService, MedicalRecordService, NotificationService, PatientService, PrescriptionService, QueueService). Stryker.NET 5.0.0 is pinned in `dotnet-tools.json`; no database, Kafka or Docker is needed.

```powershell
cd tests/MutationTesting
./run-mutation-tests.ps1                                     # all seven services
./run-mutation-tests.ps1 -Service QueueService -OpenReport   # one service, opens the HTML report
./run-mutation-tests.ps1 -Since develop                      # only code changed since develop
```

The **Mutation testing** job runs one leg per service on pull requests into `develop` or `main` (changed code only) and on manual runs (every service in full). It is not run on push or by CD. A leg fails when its service scores below its break threshold, and the legs are required checks, so a failing leg blocks the merge. Configuration, results and the reasoning behind these choices: [`tests/MutationTesting/README.md`](../tests/MutationTesting/README.md).

## Continuous deployment

CD releases to two environments in the same Azure subscription, and only during approved demo windows:

| | Development | Production |
|---|---|---|
| GitHub Environment | `azure-development` | `azure-production` |
| Automatic trigger | Successful CI on `develop` | Successful CI on `main` |
| Manual trigger | Any reviewed branch, `target=development` | `main` only, `target=production` |
| Window variable (repository) | `DEMO_WINDOW_DEVELOPMENT` | `DEMO_WINDOW_PRODUCTION` |
| Public hosts | `dev.swiftcare.me`, `api-dev.swiftcare.me` | `swiftcare.me`, `www.swiftcare.me`, `api.swiftcare.me` |
| Container Apps environment | created by Terraform | development's, shared (the subscription allows one); named by `AZURE_CONTAINERAPPS_ENVIRONMENT` and `AZURE_CONTAINERAPPS_ENVIRONMENT_RESOURCE_GROUP` |

GitHub starts a `workflow_run` workflow from the copy of `cd.yml` on the default branch (`main`), so the automatic triggers follow whatever `cd.yml` is on `main`: a change to them takes effect only after it reaches `main`. Manual runs use `cd.yml` from the selected branch.

The first job, `target`, picks the environment and reads its window variable. Unless the value is exactly `true`, the run ends there with a notice: no Environment approval is requested and Azure is not touched. A manual production run from any ref other than `main` fails in the same job. Each environment has its own concurrency queue. Open a window by setting the variable to `true` (repository Settings > Secrets and variables > Actions > Variables) and close it by setting `false` when the window ends.

`workflow_dispatch` first runs the CI quality gate for the selected branch (without mutation testing, which stays in direct CI runs) and can then deploy that branch.
CD releases only what changed. Each backend service, the gateway and the frontend is a separate deployable with its own image, migration job, Container App, smoke check and rollback. The list of deployables and their Environment value names is `deployment/cd/services.json`.

### How a release is chosen

The `plan` job compares each Container App's running commit (the SHA in its image tag) with the release commit. The frontend publishes its commit in `version.json`. Azure is the source of truth, so a change missed by an earlier failed or skipped run is picked up by the next one.

| Changed path | Releases |
|---|---|
| `services/<Name>/**` | that service |
| `ApiGateway/**` | the gateway |
| `frontend/**` | the frontend |
| `.github/workflows/cd.yml`, `deployment/cd/**`, `.dockerignore` | every deployable |
| Tests, docs, Terraform, Compose, `ci.yml`, solution files | nothing |

Terraform never runs in CD, so infrastructure changes are applied through a reviewed plan and release nothing. A running version that cannot be read (no app, a tag that is not a commit, or a commit missing from the repository history) releases that deployable, and so does an app whose latest revision never became ready, so a failed first deployment is retried by the next run. A service app that does not exist yet also releases the gateway, because the gateway needs its new internal address. The plan job writes the decision for every deployable to the run summary.

### Manual trigger

Run the CD workflow from the Actions tab (Run workflow), choose the branch, and set the inputs:

| Input | Values | Effect |
|---|---|---|
| `target` | `development` (default), `production` | Environment to release to. `production` is accepted only when the selected branch is `main`. |
| `release` | `changed` (default), `all`, `auth`, `patient`, `queue`, `medical-record`, `prescription`, `gateway`, `frontend` | `changed` uses the same selection as an automatic run. `all` releases every deployable. A name releases only that deployable, so its migration and deployment run and nothing else does. |
| `plan_only` | true or false | Reports the selection and changes nothing in Azure. |
| `bootstrap_admin` | true or false | Includes AuthService and creates the initial administrator after its migration. |
| `rollback_drill` | `none` or a deployable | Evidence only. Forces that deployable's first smoke check to fail so its rollback can be shown. It must be part of the release. |

Changes to Environment values or secrets (for example a rotated registry token or signing key) are not visible in Git, so `changed` does not detect them. Use `all` after such a change; only released apps and jobs pick up new settings.

The same run from the GitHub CLI:

```bash
gh workflow run cd.yml --ref develop -f release=prescription
gh workflow run cd.yml --ref develop -f release=changed -f plan_only=true
gh workflow run cd.yml --ref main -f target=production -f release=all -f plan_only=true
```

### Order, smoke checks and rollback

1. Each selected backend deployable builds its own image, tagged with the release commit, inside its own job. A failed build holds back only that deployable.
2. AuthService is released first, because the gateway validates every session with it and every smoke check signs in through it.
3. The other selected services are released in parallel, each in its own job. A job captures the running image, runs only its own migration job, deploys, waits for a ready revision (starting the app first when it is stopped), and smoke tests through the gateway.
4. The gateway is released last and only when no selected service failed. Its smoke test runs every service check.
5. The frontend is released separately, after the backend jobs it follows have succeeded or been skipped.

If a service fails after its deployment started, only that service returns to the image it ran before the release, and its smoke checks run again. Other services keep the versions just deployed, the gateway and frontend are skipped, and the run is marked failed. A failure before the deployment (for example a failed migration) leaves the running version unchanged.
Rollback restores images only. Schemas, data, secrets, settings and domain changes stay as they are, so migrations must stay compatible with the previous image.
The first deployment of a service has no previous image and is left as deployed when it fails. When the app already ran the release image before the run (for example `release=all` on an unchanged app), there is nothing to roll back to and the summary says so.

Smoke checks go through the gateway using two synthetic accounts (a Receptionist and a Doctor) stored as `SMOKE_RECEPTIONIST_USERNAME`, `SMOKE_RECEPTIONIST_PASSWORD`, `SMOKE_DOCTOR_USERNAME` and `SMOKE_DOCTOR_PASSWORD` in each Environment, with separate accounts per environment. Create both through the Admin user screen; the Doctor needs an unused room label of at most 16 characters and never calls patients.
The checks only read data, never print response bodies or tokens, and revoke each token afterwards.

| Deployable | Checks |
|---|---|
| Auth | Invalid login returns 401; the Receptionist signs in |
| Patient | Patient search returns a JSON array |
| Queue | Public display returns the expected shape; today's queue returns 401 without a token and an array for the Receptionist |
| MedicalRecord | The Doctor's templates return a JSON array |
| Prescription | Pending prescriptions return a JSON array for the Receptionist |
| Notification | Today's notification feed returns a JSON array for the Receptionist |
| Gateway | Health returns 200, then every check above |

A service deployed for the first time has no address in the running gateway, so a check through that gateway could only return 502. A service job therefore skips its own smoke check when the running gateway has no address for it, and the gateway job, which receives every address, runs the check for all services. After a first deployment of a service, release the gateway (`release=gateway`, or `all`).

A release starts the apps it deploys, plus AuthService and the gateway it needs for the smoke checks, when they are stopped. Starting an app does not change its image. Stop the demo apps again after the approved window.

### Adding a service

Add one entry to `deployment/cd/services.json`, create its Environment values, add its app-name variable to the environment blocks of the plan, auth, services and gateway jobs in `cd.yml`, and add its key to the `release` and `rollback_drill` choices. Add a smoke target in `deployment/cd/smoke.sh` for it. Tests fail when any of these is missing. The tests in `deployment/cd` fail when a Dockerfile has no manifest entry or an entry does not match its Dockerfile port or the gateway cluster names.

### NotificationService

NotificationService is released like the other services, from `services/NotificationService/Dockerfile`, with its own database `swiftcare_notification`, migration job and Container App. Before its first release, apply the Terraform plan that creates the database, create its scoped SQL user from inside the VNet (procedure in the [Terraform README](../deployment/terraform/README.md)), and set these `azure-development` values: variables `AZURE_NOTIFICATION_APP`, `AZURE_NOTIFICATION_MIGRATE_JOB` and `NOTIFICATION_DB_USER`, and secret `NOTIFICATION_DB_PASSWORD`. The plan job needs `AZURE_NOTIFICATION_APP` for every release, so create it before the next run. After the first image push, give the registry pull token read access to the `swiftcare-notification` package before the first migration job. The first release creates the app and the gateway together, because the gateway needs the new internal address.

### Local checks

```bash
python3 -m unittest discover -s deployment/cd -p "test_*.py"
bash deployment/cd/test-scripts.sh
```

The first checks the selection rules and summary. The second runs the deployment scripts against a fake Azure CLI and a stub gateway, so it proves script logic and not Azure behavior. It needs bash, jq, curl and Python 3.

CD sets both minimum and maximum replicas to `1` for the gateway and all six services on both creation and update. Running apps therefore keep a replica available when idle, avoiding scale-from-zero startup delays. For the shutdown procedure, see [deployment cost controls](../deployment/terraform/README.md#cost-controls).

The Azure messaging prerequisite follows the repository architecture: one private Azure Container Instances group contains separate ZooKeeper and Confluent Kafka 7.6.1 containers. Kafka connects to ZooKeeper at `localhost:2181` because containers in the same group share a network namespace. `KAFKA_BOOTSTRAP_SERVERS` must instead contain the private broker address reachable from the Container Apps environment; local Compose addresses such as `kafka:29092` are rejected.

CD passes `APPLICATIONINSIGHTS_CONNECTION_STRING` to every service and the gateway (`deployment/cd/services.json`). When it is set, each process exports traces and logs to Azure Application Insights through OpenTelemetry; when it is empty, telemetry stays off.

## Observability policy

Every service must eventually expose `/health`, emit structured logs, measure request failures and latency, record Kafka producer and consumer failures, and propagate correlation IDs through HTTP calls and Kafka events.

The API Gateway must expose `/health`, log incoming requests with correlation IDs, and record JWT validation failures, gateway-authentication failures, and routing errors. It must remove client-supplied identity and gateway-secret headers before attaching trusted forwarding headers.

Logs must never include passwords, JWT tokens, database connection strings, access credentials, or sensitive patient and medical information.

## GitHub Environments and secrets

`.env.example` contains safe local placeholders. Real passwords, tokens, JWT keys, Azure credentials, and connection strings must remain in local `.env` files or approved secret stores.

The CD workflow reads deployment configuration from the GitHub Environment of its target, `azure-development` or `azure-production`; both use the same variable and secret names with their own values, and no secret value is shared between them except, optionally, the registry pull token. Non-sensitive Azure resource identifiers, region, application/job/container names, endpoints, JWT issuer/audience, database username, frontend origins, and initial administrator display name belong in environment variables. Passwords, registry credentials, JWT signing material, gateway trust material, the Static Web Apps deployment token, and optional bootstrap credentials belong in environment secrets.

Azure authentication uses GitHub OIDC through `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, and `AZURE_SUBSCRIPTION_ID`; do not create a long-lived `AZURE_CREDENTIALS` secret. Custom-domain deployment requires `GATEWAY_ORIGIN`, `FRONTEND_ORIGIN`, and `FRONTEND_WWW_ORIGIN` to contain HTTPS origins without trailing slashes. AuthService, PatientService, and QueueService use separate non-administrator MySQL accounts restricted to `swiftcare_auth`, `swiftcare_patient`, and `swiftcare_queue` respectively, never the flexible-server administrator account.

PatientService deployment requires `AZURE_PATIENT_APP`, `AZURE_PATIENT_MIGRATE_JOB`, and `PATIENT_DB_USER` environment variables plus the `PATIENT_DB_PASSWORD` environment secret. QueueService deployment requires `AZURE_QUEUE_APP`, `AZURE_QUEUE_MIGRATE_JOB`, `QUEUE_DB_USER`, `KAFKA_PATIENT_CHECKED_IN_TOPIC`, and `KAFKA_QUEUE_CONSUMER_GROUP` environment variables plus the `QUEUE_DB_PASSWORD` environment secret. MedicalRecordService deployment requires `AZURE_MEDICAL_RECORD_APP` (for example, `swiftcare-medical-record`), `AZURE_MEDICAL_RECORD_MIGRATE_JOB` (for example, `swiftcare-medical-record-migrate`), and `MEDICAL_RECORD_DB_USER` environment variables plus the `MEDICAL_RECORD_DB_PASSWORD` environment secret. The medical-record account needs privileges only on `swiftcare_medical_record`; create that account inside the Azure VNet before running CD and keep its password in the `azure-development` GitHub Environment secret. Review and apply the Terraform plan that creates `swiftcare_medical_record` first. If the database already exists outside Terraform state, import it instead of attempting to create it again. Production uses the separate `azure-production` Environment, which requires a reviewer and accepts only `main`.

The complete list of values each deployable needs is in `deployment/cd/services.json`.
