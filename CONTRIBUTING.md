# Contributing to SwiftCare

## Branching

- `main` holds stable, reviewed, releasable code and deploys to production.
- `develop` integrates completed work and deploys to development.
- Work happens on short-lived branches from `develop`, merged back through pull requests.
- Only `develop` normally merges into `main`, through a separate release pull request. An urgent production fix may go straight to `main`, and `main` must then be merged back into `develop`.

Both permanent branches require a pull request, approval from the sprint's QA role, resolved conversations and passing required checks. Force pushes and deletion are blocked.

### Branch names

Use the type and the exact Jira key:

```text
feature/SWC-6-user-login
fix/SWC-12-fix-patient-search
refactor/SWC-6-improve-authentication
docs/SWC-9-update-registration-documentation
test/SWC-19-add-queue-tests
```

Fixes for GitHub issues use the issue number, for example `fix/124-failed-allocation-not-already-queued`. Work not tied to a story or issue may omit the key, for example `chore/ci-dotnet-build-test`.

## Commit messages

Use [Conventional Commits](https://www.conventionalcommits.org/) with the Jira key or GitHub issue as the scope. Allowed types are `feat`, `fix`, `refactor`, `docs`, `test`, `style`, `perf` and `chore`.

```text
feat(SWC-6): implement user login
fix(SWC-12): correct patient search validation
test(SWC-19): add queue event tests
fix(#124): fail a check-in that could not allocate a queue number
docs: add daily stand-up log template
```

Keep commits atomic: each commit builds, and a fix is never bundled into a feature commit. Unit tests for a story's test subtask go in their own `test(SWC-<subtask>)` commits after the code they cover.

## Pull requests

1. Create a branch from the latest `develop`.
2. Commit using the convention above.
3. Before pushing, merge the latest `develop`, build the whole solution in Release and run the tests:

   ```bash
   dotnet build SwiftCare.slnx --configuration Release
   dotnet test SwiftCare.slnx
   cd frontend && npm run lint && npm run build
   ```

4. For a changed backend service, run its mutation tests on the changed code:

   ```powershell
   cd tests/MutationTesting
   ./run-mutation-tests.ps1 -Service <ServiceName> -Since origin/develop
   ```

5. Open a pull request into `develop` with a summary, the changes, how it was tested, any setup (migrations, configuration) and steps for QA.
6. Get approval from the sprint's QA role and a green CI run, then merge.

The GitHub for Jira integration links branches, commits and pull requests that contain a Jira key.

## Definition of done

- The Jira acceptance criteria are met and the story description matches what was built.
- Unit tests cover the new behaviour, including failure and edge cases.
- CI is green: build, tests, coverage, SonarQube Cloud quality gate, mutation thresholds, end-to-end tests and security scans.
- The service README documents new endpoints, configuration and behaviour.
- QA has approved the pull request.

## Data and service boundaries

Each service owns its entities, `DbContext`, MySQL database and committed `Migrations` folder:

| Service | DbContext | Database |
| --- | --- | --- |
| AuthService | AuthDbContext | `swiftcare_auth` |
| PatientService | PatientDbContext | `swiftcare_patient` |
| QueueService | QueueDbContext | `swiftcare_queue` |
| MedicalRecordService | MedicalRecordDbContext | `swiftcare_medical_record` |
| PrescriptionService | PrescriptionDbContext | `swiftcare_prescription` |
| NotificationService | NotificationDbContext | `swiftcare_notification` |

A service must never query or modify another service's database. Share information through Kafka events or through the owning service's API via the gateway.

Migrations never run at container startup. Every service applies them through its `--migrate` maintenance command, run as a separate step locally and as a migration job in CD. Migrations must stay compatible with the previous image, because a rollback restores images but not schemas.

## Configuration and secrets

`.env.example` holds safe local placeholders. Real passwords, keys, tokens and connection strings stay in local `.env` files or GitHub Environment secrets, and are never committed, printed in logs or pasted into issues and chats.

`Jwt__SecretKey`, `Jwt__Issuer`, `Jwt__Audience` and `Gateway__InternalSecret` must be identical in the gateway and every service; a mismatch shows up as a `401` at sign-in.

## Logging

Never log passwords, tokens, connection strings or patient and clinical information. Log identifiers and error types, not exception messages that may contain data, and sanitise any client-supplied value before logging it.
