# Mutation Testing Result - 2026-10-11 (SWC-151)

Full re-run of all seven backend services against `develop` at cb1012d, with the same settings
as before: Stryker.NET 5.0.0, `Standard` mutation level, per-test coverage analysis, the same
exclusions. About ten pull requests had been merged since the 2026-10-05 run (clinical visit
validation, session validation, user management and audit log, outbox delivery, call-next
retries, the #124 fix and others), so the earlier results no longer described the code.

The run was done twice: once on the code as merged, then again after tests were added for the
real gaps that the new code introduced. Only test files changed between the two runs. No
production code was changed.

This report supersedes `RESULT-mutation-20261005.md` and
`RESULT-mutation-notificationservice-20261011.md`, which are kept as the history of the
earlier runs.

## Result

| Service | 2026-10-05 final | Before new tests | **Final** | Covered score (final) | Undetected (before → final) |
|---|---:|---:|---:|---:|---:|
| ApiGateway | 96.49% | 94.35% | **98.26%** | 98.69% | 13 → 4 |
| AuthService | 86.03% | 89.07% | **91.96%** | 98.62% | 34 → 25 |
| MedicalRecordService | 91.71% | 95.33% | **95.33%** | 99.78% | 22 → 22 |
| NotificationService | 88.57%* | 89.04% | **89.04%** | 97.99% | 24 → 24 |
| PatientService | 90.88% | 90.64% | **91.30%** | 97.85% | 28 → 26 |
| PrescriptionService | 90.32% | 90.74% | **91.67%** | 98.02% | 30 → 27 |
| QueueService | 88.97% | 90.27% | **91.45%** | 97.48% | 33 → 29 |
| **All services** | **90.25%** (six) | **91.61%** | **92.84%** | **98.45%** | **184 → 157** |

\* NotificationService was added on 2026-10-11 (`RESULT-mutation-notificationservice-20261011.md`).

- Final run: 3,701 mutants, 2,193 valid; 2,032 killed, 4 timeouts, 32 survived, 125 not
  covered. 399 compile errors (stillborn) and 1,109 ignored are excluded from the score.
- Unit test cases: 1,750 before the new tests, 1,778 after (28 added). The first run on
  2026-10-05 started from 630.
- Every service is in the `High` band. All 157 remaining undetected mutants are classified
  below.

Overall progress since mutation testing was introduced:

| | 2026-10-05 baseline | 2026-10-05 final | 2026-10-11 final |
|---|---:|---:|---:|
| Services | 6 | 6 | 7 |
| Mutation score | 62.11% | 90.25% | **92.84%** |
| Covered score | 77.56% | 98.24% | **98.45%** |
| Unit test cases | 630 | 983 | 1,778 |

Reports: `full-before-20261011/` (before the new tests) and the per-service folders (final),
local and git-ignored, regenerated with `run-mutation-tests.ps1`.

## The #124 defect is confirmed fixed

The 2026-10-05 run found that `QueueEntryCreationService` reported a check-in whose queue number
could not be allocated as "already queued", so the patient was never queued (finding 1 of that
report, raised as GitHub issue #124). The fix was merged in PR #129. In this run all four mutants
that pointed at the defect are gone: lines 73 and 97-98 of the old code are killed or no longer
exist. Mutation testing found the defect, and the same analysis now confirms the fix is tested.

## What the new code brought in

39 mutants that did not exist on 2026-10-05 were undetected, all in code merged since then.
Each was classified: 27 were real test gaps, 12 were equivalent or log-only.

| Weakness | Where | Mutants |
|---|---|---:|
| Error message checked only in part | `ClinicalVisitValidationMiddleware`: a removed `return` wrote a second response after the first, and `Assert.Contains` on a message fragment still passed | 2 |
| Exception filter one side only | `ClinicalVisitValidationMiddleware` line 84: an unexpected exception was never tested, so `&&` → `\|\|` (report every failure as 503) survived | 1 |
| Configuration errors | Both Gateway validators: a missing proxy cluster (would throw `NullReferenceException`, an unhandled 500, instead of a handled 503) and an empty destination list were never tested | 6 |
| Request defaults | `ResetPasswordRequest` and `UpdateUserRequest`: a non-empty default would let a request without the field pass `[Required]`, so an omitted password could be "reset" to the default | 2 |
| Forwarded request data | The client IP or `"unknown"` recorded for admin actions | 2 |
| Ordering | Audit log newest-first per source (2); outbox delivery tie-break by ID in PatientService and QueueService (2) | 4 |
| Boundaries | Session token ID of exactly 128 characters; a malformed session request must be rejected before cleanup work | 2 |
| Concurrency outcomes | PrescriptionService: a decision recorded concurrently (thrown instead of reported), the `==` in that check, a concurrent medicine change reported as success | 3 |
| Message assertions | Audit log 403 message; birth-date validation without its date service | 2 |
| Time zone handling | `QueueCompletionService` line 64: how a Local, UTC or Unspecified completion time is stored | 3 |

## Tests added

| Service | File | What it covers |
|---|---|---|
| ApiGateway | `Middleware/ClinicalVisitValidationEdgeCaseTests` | Each rejection writes exactly one response with its own message (the body must parse as a single JSON object); an unexpected validator failure propagates instead of becoming a 503 |
| ApiGateway | `Security/ValidatorDestinationEdgeCaseTests` | Missing cluster and empty destination list for both `ClinicalVisitValidator` and `SessionValidator`: exact exception type and message |
| AuthService | `Controllers/RequestContextForwardingTests` (additions) | Admin action IP or `"unknown"`; empty reset-password and edit-user requests fail `[Required]` |
| AuthService | `Controllers/AuditLogsControllerEdgeCaseTests` | 403 with the exact `Forbidden` message, service never called |
| AuthService | `Services/AuditLogServiceEdgeCaseTests` | The limit keeps the newest sign-outs and admin actions |
| AuthService | `Services/SessionValidationEdgeCaseTests` | Token ID of exactly 128 characters accepted; a request with an empty user or session version is rejected before expired sessions are cleaned up |
| PatientService | `Services/PatientServiceEdgeCaseTests` (addition) | Birth-date validation without `IClinicDateProvider` names the missing service |
| PatientService, QueueService | `Services/OutboxDeliveryEdgeCaseTests` | Events created at the same moment are delivered in ID order |
| PrescriptionService | `Services/PrescriptionManagementServiceEdgeCaseTests` (additions) | A decision recorded between the checks and the save is reported (simulated with a `SaveChangesInterceptor`); another consultation's decision does not change a duplicate outcome; a concurrent medicine change returns `ConcurrentModification` |
| QueueService | `Services/QueueCompletionServiceEdgeCaseTests` | A Local completion time is converted to UTC; UTC and Unspecified times are stored unchanged |

The three `QueueCompletionService` mutants depend on the machine's time zone. They are killed
where local time is not UTC (this run: Asia/Colombo, UTC+05:30). On a UTC machine, such as the
GitHub Actions runners, local and UTC times are the same, the mutants cannot be told apart, and
they survive there. The tests pass in every time zone.

## Remaining undetected mutants

All 157 are accounted for.

| Category | Survived | No coverage | Mutants |
|---|---:|---:|---|
| **Integration-only code** | 0 | 121 | `MaintenanceCommandRunner.RunAsync`/`MigrateAsync` in six services apply EF migrations to MySQL and are verified by CI's `validate-migrations` job |
| **Equivalent mutants** | 20 | 2 | See below |
| **Log-only effects** | 9 | 2 | `LogRejection(...)` calls (AuthService `UserAccountService` 28, 40, 55, 81; PatientService `PatientRegistrationService` 44, 82), the publish-failure log branch (PatientService 89), outbox exception messages that are only logged (PatientService and QueueService `OutboxDelivery` 17), and the correlation ID `PatientCheckedInConsumer` only writes to logs (QueueService 160, 162). Behaviour is unchanged; log calls are excluded by policy |
| **Configuration defaults** | 3 | 0 | `string.Empty` defaults of `MedicalRecordOptions` and of `ClinicOptions` in PatientService and PrescriptionService. The real values always come from configuration |

Equivalent mutants (no test can tell them apart from the original):

| Service | Location | Mutant | Why it is equivalent |
|---|---|---|---|
| ApiGateway | `ClinicalVisitValidationMiddleware` 14 | `?? ""` → `?? "Stryker was here!"` | A request with no path matches no clinical route either way, so it is passed on |
| ApiGateway | `ClinicalVisitValidationMiddleware` 38 | Buffer threshold `30 * 1024` → `30 / 1024` | Only decides whether the buffered body is held in memory or in a temporary file |
| ApiGateway | `GatewayForwardingMiddleware` 14 | `"X-Gateway-Secret"` removed from the strip list | The header is overwritten with the gateway's own secret straight after |
| ApiGateway | `RevokedTokenStore` 30 | `<=` → `<` on expiry | Only differs when expiry equals now to the tick; not reachable without injecting a clock |
| AuthService | `SessionValidationService` 20 | Expired-session cleanup ordered newest first | Every expired session is invalid; which of them a batch removes first changes no validation result, and later requests remove the rest |
| NotificationService | `NotificationEventParser` 56 | `catch (JsonException)` block emptied | Stryker completes it with `return default`, which is `null`, the value the original returns |
| NotificationService | `NotificationEventParser` 148 | `<=` → `<` on the diagnosis length | A trimmed diagnosis of exactly 200 characters is cut at 200 and trimmed again: the same string |
| NotificationService | `NotificationRecorder` 18 | Duplicate pre-check removed | The unique index on `EventId` rejects the insert and the `catch` returns `Duplicate` |
| NotificationService | `ActivityEventConsumer` 144 | `base.Dispose()` removed | The host always stops the worker before disposing it, and stopping already cancels the loop |
| PatientService | `PatientSearchService` 32 | `?? string.Empty` → `?? "Stryker was here!"` | Both produce an empty result for a missing term |
| PrescriptionService | `PrescriptionManagementService` 54 | No-prescription pre-check removed | The consultation's outcome claim (primary key on `ConsultationOutcomes`) rejects the save and the `catch` returns the same outcome |
| PrescriptionService | 96, 227, 345 | `ChangeTracker.Clear()` removed in a `catch` that returns | The DbContext is scoped to the request and is not used again after the method returns |
| PrescriptionService | 325, 326 | `Items.Remove` or `PrescriptionItems.Remove` removed | EF Core removes a deleted dependent from the navigation after `SaveChanges`, and deletes an orphaned required dependent |
| QueueService | `CallNextPatientService` 149 | `transaction.DisposeAsync()` removed | The transaction is already committed; `await using` disposes it at the end of the scope instead |
| QueueService | `CallNextPatientService` 163 | `<=` → `<` on the base delay | With a zero base delay both return `TimeSpan.Zero` (zero multiplied by anything is zero) |
| QueueService | `QueueEntryCreationService` 82 | `RollbackAsync` removed | `await using` disposes the uncommitted transaction, which rolls it back |
| QueueService | `QueueEntryCreationService` 167 | Message of the wrapping exception emptied | Caught inside the same method; never observable |
| QueueService | `PatientQueueStatusService` 46 | `{ IsCheckedIn = false }` → `{}` | `false` is the default |
| QueueService | `ConsultationCompletedConsumer` 146 | `base.Dispose()` removed | The host always stops the worker before disposing it |

## Thresholds

The rule is unchanged: `break` is 5 points below each final score, rounded down to 5; `low`
equals `break` and `high` is 5 points above it. Three services scored high enough to raise
their break threshold:

| Service | high | low | break | Final score | Change |
|---|---:|---:|---:|---:|---|
| ApiGateway | 95 | 90 | 90 | 98.26% | - |
| AuthService | 90 | 85 | 85 | 91.96% | raised from 80 |
| MedicalRecordService | 95 | 90 | 90 | 95.33% | raised from 85 |
| NotificationService | 85 | 80 | 80 | 89.04% | - |
| PatientService | 90 | 85 | 85 | 91.30% | - |
| PrescriptionService | 90 | 85 | 85 | 91.67% | - |
| QueueService | 90 | 85 | 85 | 91.45% | raised from 80 |

QueueService still clears its break threshold on a UTC runner, where the three time zone
mutants survive (90.56%).

## Notes on the run

- Full run of all seven services: about 25 minutes on a 22-core laptop. ApiGateway takes about
  14 of them, because its route tests start an in-memory web host per test.
- In QueueService's Kafka consumers, NotificationService's consumer and
  `NoPrescriptionService.RecordAsync`, Stryker's "safe mode" drops every mutant in methods where
  a mutation trips the C# definite-assignment check (CS0165). These count as compile errors.
- Stop the Docker Compose stack before a run: one ApiGateway route test fails while a real
  PrescriptionService is listening on port 5001.
