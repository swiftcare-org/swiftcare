# Mutation Testing Result - 2026-10-05 (SWC-151)

Stryker.NET 5.0.0, `Standard` mutation level, per-test coverage analysis, all six backend
services. Baseline run before any test was added, final run after. Same configuration and
scope for both runs; only test files changed in between. No production code was changed.

## Result

| Service | Mutation score (baseline → final) | Covered score (baseline → final) | Undetected (baseline → final) | Test cases (baseline → final) |
|---|---|---|---|---|
| ApiGateway | 73.68% → **96.49%** | 73.68% → 96.49% | 15 → 2 | 117 → 127 |
| AuthService | 64.25% → **86.03%** | 73.72% → 97.47% | 64 → 25 | 73 → 107 |
| MedicalRecordService | 51.55% → **91.71%** | 72.36% → 99.72% | 187 → 32 | 88 → 217 |
| PatientService | 78.10% → **90.88%** | 85.94% → 98.42% | 60 → 25 | 200 → 238 |
| PrescriptionService | 52.42% → **90.32%** | 77.38% → 98.25% | 118 → 24 | 64 → 138 |
| QueueService | 65.84% → **88.97%** | 78.39% → 96.90% | 96 → 31 | 88 → 156 |
| **All services** | **62.11% → 90.25%** | **77.56% → 98.24%** | **540 → 139** | **630 → 983** |

- **Mutation score** = (killed + timeout) / (killed + timeout + survived + no coverage).
- **Covered score** leaves out mutants no test reaches. It rates the assertions of the tests that exist.
- Every service ends in the `High` band (≥ 80%). The remaining 139 undetected mutants are all
  classified below; none is an unexplained test gap.

Reports: `final-20261005/` and `baseline-20261005/` (local, git-ignored), regenerated with
`run-mutation-tests.ps1`.

## What the baseline showed

| Weakness | Where | Mutants |
|---|---|---:|
| Security middleware with no or weak tests | `GatewaySecretMiddleware` in all five services. PrescriptionService: 0 of 25 mutants reached by any test | ~60 |
| Status code asserted, message not | 403/401/404/409 responses across controllers. A changed message (part of the API contract the frontend shows) went unnoticed | ~100 |
| Missing-header and malformed-header cases | `FirstOrDefault()` → `First()` survived wherever a test always sent the header | ~30 |
| One side of a compound condition never tested | `!TryParse(id) \|\| id == Guid.Empty` → `&&`, BP pairing, soft-delete filters | ~40 |
| Ordering tie-breaks never exercised | `ThenBy` ↔ `ThenByDescending` on ids, names, rooms; `Q-999` vs `Q-1000` | 8 |
| Persistence not verified | `SaveChanges` removed with no test failing (EF InMemory returns the tracked entity) | 4 |
| Untested controllers and failure paths | MedicalRecordService follow-up and vital-signs controllers, Kafka publisher, retry loops | ~90 |

## Tests added

| Service | Files | What they cover |
|---|---|---|
| All five services | `Middleware/GatewaySecretMiddlewareTests.cs` | Valid secret; missing, empty, wrong, shorter and longer secret; secret not configured; public paths and paths that only resemble them; the 401 body |
| PrescriptionService | `PrescriptionManagementServiceEdgeCaseTests`, `PrescriptionsControllerEdgeCaseTests`, additions to 3 files | Guard clauses, id tie-breaks, a concurrent duplicate simulated with a `SaveChangesInterceptor`, a non-duplicate save failure, every outcome mapping and message |
| QueueService | `QueueControllerEdgeCaseTests`, `CallNextPatientServiceEdgeCaseTests`, `TodayQueueServiceEdgeCaseTests`, `QueueEntryCreationServiceEdgeCaseTests`, `KafkaOptionsTests`, additions to 3 files | Correlation IDs, `Q-999`/`Q-1000` ordering, other days' entries, stale tracked state after a failed publish, concurrency retries and the counter-creation race (interceptor test double), redelivery bookkeeping, consumer disposal, the topic contract with MedicalRecordService |
| MedicalRecordService | `DoctorEndpointGuardTests` (10 endpoints × 5 cases), `ControllerOutcomeMappingTests`, `ServiceGuardClauseTests`, `KafkaConsultationCompletedPublisherTests`, `CreateConsultationRequestValidationTests`, `LogSanitizerTests`, additions to 1 file | Role and doctor-id guards, every outcome including the validation-problem response, strict-mock guard clauses, publish success, broker rejection and timeout |
| AuthService | `RequestContextForwardingTests`, `BootstrapAdminOutputTests`, additions to 2 files | Correlation ID and client IP forwarding with fallbacks, the deactivated-login audit entry, the room-number claim rule, bootstrap boundary and operator messages (non-parallel collection, because `Console` redirection is process-wide) |
| PatientService | `PatientControllerEdgeCaseTests`, `PatientServiceEdgeCaseTests` | Missing headers, not-found and publish-failure responses, persistence checked through a second `DbContext`, soft-deleted patients, ordering, search length limits, date-validation boundaries |
| ApiGateway | `GatewayMiddlewareEdgeCaseTests` | Correlation ID generation, reuse and the logging scope (capturing test logger), forged name and room headers, full-name claim forwarding, the revocation 401 body |

## Findings

1. **Defect: a failed queue allocation is reported as "already queued".** In
   `QueueEntryCreationService.CreateQueueEntryAsync`, the last retry's
   `DbUpdateConcurrencyException` fails the `when (attempt < MaxAllocationAttempts)` filter and
   falls into the general `catch (DbUpdateException)`, which returns `AlreadyQueuedToday`. The
   `throw` after the loop (line 97) is unreachable. Under sustained contention a patient is
   reported as queued but has no queue entry. Mutation testing exposed it twice: line 97 had no
   coverage that no test could provide, and the mutant `attempt < Max` → `attempt <= Max` on
   line 73 survives because it fixes the defect. The test
   `StopsAfterTheConfiguredNumberOfAttemptsWithoutQueueingThePatient` asserts only what is
   correct either way. To be raised as a bug.
2. **Security middleware was barely tested.** The gateway-secret check that protects every
   service had no unit tests in PrescriptionService and incomplete ones elsewhere. It is now
   covered identically in all five services.
3. **An environment-dependent test.** `PrescriptionHistoryAndReportRouteTests` (ApiGateway)
   fails while the Docker Compose stack is running, because the proxied request reaches the real
   PrescriptionService on port 5001. Stop the stack before running the Gateway tests or Stryker.

## Remaining undetected mutants

All 139 are accounted for.

| Category | Survived | No coverage | Mutants |
|---|---:|---:|---|
| **Integration-only code** | 0 | 110 | `MaintenanceCommandRunner.RunAsync`/`MigrateAsync` in five services (101) apply EF migrations to MySQL and are verified by CI's `validate-migrations` job. `ConsultationTemplateService` (9) reads through a concrete `MySqlConnection`. Unit-testing either needs a database or a production-code change |
| **Equivalent mutants** | 12 | 0 | See below |
| **Log-only effects** | 6 | 3 | `LogRejection(...)` calls (AuthService ×3+1, PatientService ×1+1), the publish-failure log branch (PatientService `published`), and the correlation ID that `PatientCheckedInConsumer` only writes to logs (×1+1). Behaviour is unchanged; log calls are excluded by policy (`ignore-methods`) and these are wrapper helpers |
| **Configuration and entity defaults** | 4 | 1 | `string.Empty` defaults of `ClinicOptions`/`MedicalRecordOptions` (real values always come from configuration), `User.IsActive = true` (every creator sets it explicitly), `ConsultationTemplate.IsActive` (used only by excluded seed data) |
| **The defect in finding 1** | 1 | 2 | `QueueEntryCreationService` lines 73, 97, 98 |

Equivalent mutants (no test can tell them apart from the original):

| Service | Line | Mutant | Why it is equivalent |
|---|---:|---|---|
| PrescriptionService | `PrescriptionManagementService` 40 | Duplicate pre-check removed | The unique index on `ConsultationId` rejects the insert and the `catch` returns the same outcome |
| PrescriptionService | 283, 284 | `Items.Remove` or `PrescriptionItems.Remove` removed | EF Core removes a deleted dependent from the navigation after `SaveChanges`, and deletes an orphaned required dependent |
| QueueService | `CallNextPatientService` 126; `QueueEntryCreationService` 78, 87 | `RollbackAsync` removed | `await using` disposes the uncommitted transaction, which rolls it back |
| QueueService | `QueueEntryCreationService` 149 | Message of the wrapping exception emptied | Caught inside the same method; never observable |
| QueueService | `PatientQueueStatusService` 43 | `{ IsCheckedIn = false }` → `{}` | `false` is the default |
| QueueService | `ConsultationCompletedConsumer` 139 | `base.Dispose()` removed | The host always stops the worker before disposing it |
| PatientService | `PatientSearchService` 32 | `?? string.Empty` → `?? "Stryker was here!"` | Both produce an empty result for a missing term |
| ApiGateway | `GatewayForwardingMiddleware` 14 | `"X-Gateway-Secret"` removed from the strip list | The header is overwritten with the gateway's own secret straight after |
| ApiGateway | `RevokedTokenStore` 30 | `<=` → `<` on expiry | Only differs when expiry equals `DateTimeOffset.UtcNow` to the tick; not reachable without injecting a clock |

## Thresholds

`break` was 0 for the baseline so it could not fail. It is now set 5 points below each final
score, rounded down to 5, so CI fails if the suite is weakened:

| Service | high | low | break |
|---|---:|---:|---:|
| ApiGateway | 80 | 60 | 90 |
| AuthService | 80 | 60 | 80 |
| MedicalRecordService | 80 | 60 | 85 |
| PatientService | 80 | 60 | 85 |
| PrescriptionService | 80 | 60 | 85 |
| QueueService | 80 | 60 | 80 |

## Notes on the run

- Full run of all six services: about 16 minutes on a 22-core laptop. ApiGateway takes about
  10 of them, because its route tests start an in-memory web host per test.
- `CompileError` mutants (259) are stillborn: they do not compile and are excluded from the
  score. In QueueService's Kafka consumers, Stryker's "safe mode" drops every mutant in the
  methods where a mutation trips the C# definite-assignment check (CS0165).
- `Ignored` mutants (865) are the excluded files (`Migrations/`, `Data/`, `Program.cs`), logging
  calls, and mutants inside blocks already removed by a block-removal mutant.
